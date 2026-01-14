using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace GridBanner
{
    public sealed class AlertManager : IDisposable
    {
        public event EventHandler<AlertMessage?>? AlertChanged;

        private readonly object _gate = new();
        private readonly JsonSerializerOptions _jsonOptions = new()
        {
            PropertyNameCaseInsensitive = true
        };

        private string? _filePath;
        private string? _url;
        private TimeSpan _pollInterval = TimeSpan.FromSeconds(5);
        private HashSet<string>? _workstationSites;  // null = no filtering (backward compatible)
        private HashSet<string>? _systemGroups;  // Azure/Entra groups this system belongs to (for group-based filtering)
        private SystemInfo? _systemInfo;  // System info to send when polling
        private string? _baseUrl;  // Base URL for downloading audio files
        
        public string? BaseUrl => _baseUrl;

        private FileSystemWatcher? _watcher;
        private Timer? _debounceTimer;
        private Timer? _urlTimer;
        private HttpClient? _httpClient;

        private CancellationTokenSource? _cts;

        private AlertMessage? _current;
        private DateTime? _lastSuccessfulConnection;
        
        public DateTime? LastSuccessfulConnection => _lastSuccessfulConnection;

        public record SystemInfo(
            string WorkstationName,
            string Username,
            string Classification,
            string Location,
            string Company,
            string BackgroundColor,
            string ForegroundColor,
            int ComplianceStatus
        );

        public void Configure(string? alertFileLocation, string? alertUrl, TimeSpan pollInterval, string? workstationSiteNames = null, SystemInfo? systemInfo = null, List<string>? systemGroups = null)
        {
            ConfigureInternal(alertFileLocation, alertUrl, pollInterval, workstationSiteNames, systemInfo, systemGroups);
        }
        
        /// <summary>
        /// Update system groups without full reconfiguration.
        /// </summary>
        public void UpdateSystemGroups(List<string>? systemGroups)
        {
            if (systemGroups != null && systemGroups.Count > 0)
            {
                _systemGroups = new HashSet<string>(systemGroups, StringComparer.OrdinalIgnoreCase);
            }
            else
            {
                _systemGroups = null;
            }
        }
        
        private void ConfigureInternal(string? alertFileLocation, string? alertUrl, TimeSpan pollInterval, string? workstationSiteNames = null, SystemInfo? systemInfo = null, List<string>? systemGroups = null)
        {
            _filePath = string.IsNullOrWhiteSpace(alertFileLocation) ? null : alertFileLocation.Trim();
            _url = string.IsNullOrWhiteSpace(alertUrl) ? null : alertUrl.Trim();
            _pollInterval = pollInterval <= TimeSpan.Zero ? TimeSpan.FromSeconds(5) : pollInterval;
            _systemInfo = systemInfo;
            
            // Dispose existing HttpClient and create a new one with the correct timeout
            // This is necessary because HttpClient.Timeout cannot be changed after the first request
            try { _httpClient?.Dispose(); } catch { /* ignore */ }
            
            // Set HTTP timeout to 80% of poll interval, but cap at 0.5 seconds minimum and 2 seconds maximum
            // This ensures we detect failures quickly without being too aggressive
            var timeoutSeconds = Math.Max(0.5, Math.Min(_pollInterval.TotalSeconds * 0.8, 2.0));
            _httpClient = new HttpClient
            {
                Timeout = TimeSpan.FromSeconds(timeoutSeconds)
            };
            
            // Extract base URL for audio file downloads
            if (!string.IsNullOrWhiteSpace(alertUrl) && Uri.TryCreate(alertUrl, UriKind.Absolute, out var uri))
            {
                _baseUrl = $"{uri.Scheme}://{uri.Host}:{uri.Port}";
            }
            else
            {
                _baseUrl = null;
            }

            // Parse comma-separated site names (case-insensitive)
            if (string.IsNullOrWhiteSpace(workstationSiteNames))
            {
                _workstationSites = null;  // No filtering - show all alerts
            }
            else
            {
                _workstationSites = new HashSet<string>(
                    workstationSiteNames.Split(',', StringSplitOptions.RemoveEmptyEntries)
                        .Select(s => s.Trim())
                        .Where(s => !string.IsNullOrWhiteSpace(s))
                        .Select(s => s.ToLowerInvariant()),
                    StringComparer.OrdinalIgnoreCase
                );
            }
            
            // Store system's Azure/Entra groups for group-based filtering
            if (systemGroups != null && systemGroups.Count > 0)
            {
                _systemGroups = new HashSet<string>(systemGroups, StringComparer.OrdinalIgnoreCase);
            }
            else
            {
                _systemGroups = null;
            }
        }

        public void Start()
        {
            Stop();

            _cts = new CancellationTokenSource();

            if (!string.IsNullOrWhiteSpace(_filePath))
            {
                StartFileWatcher(_filePath!);
            }

            if (!string.IsNullOrWhiteSpace(_url))
            {
                _urlTimer = new Timer(async _ => await EvaluateAsync().ConfigureAwait(false), null, TimeSpan.Zero, _pollInterval);
            }
            else
            {
                // Still do an initial evaluation (for file path).
                _ = EvaluateAsync();
            }
        }

        public void Stop()
        {
            try { _cts?.Cancel(); } catch { /* ignore */ }
            try { _cts?.Dispose(); } catch { /* ignore */ }
            _cts = null;

            try { _watcher?.Dispose(); } catch { /* ignore */ }
            _watcher = null;

            try { _debounceTimer?.Dispose(); } catch { /* ignore */ }
            _debounceTimer = null;

            try { _urlTimer?.Dispose(); } catch { /* ignore */ }
            _urlTimer = null;
        }

        private void StartFileWatcher(string path)
        {
            var dir = Path.GetDirectoryName(path);
            var file = Path.GetFileName(path);
            if (string.IsNullOrWhiteSpace(dir) || string.IsNullOrWhiteSpace(file))
            {
                return;
            }

            if (!Directory.Exists(dir))
            {
                // Directory may not exist yet; we still rely on periodic URL polling (if any),
                // or initial evaluation will clear.
                return;
            }

            _watcher = new FileSystemWatcher(dir, file)
            {
                NotifyFilter = NotifyFilters.LastWrite | NotifyFilters.FileName | NotifyFilters.CreationTime | NotifyFilters.Size
            };

            _watcher.Changed += (_, __) => DebounceEvaluate();
            _watcher.Created += (_, __) => DebounceEvaluate();
            _watcher.Deleted += (_, __) => DebounceEvaluate();
            _watcher.Renamed += (_, __) => DebounceEvaluate();
            _watcher.EnableRaisingEvents = true;
        }

        private void DebounceEvaluate()
        {
            lock (_gate)
            {
                _debounceTimer ??= new Timer(async _ => await EvaluateAsync().ConfigureAwait(false), null, Timeout.InfiniteTimeSpan, Timeout.InfiniteTimeSpan);
                _debounceTimer.Change(TimeSpan.FromMilliseconds(250), Timeout.InfiniteTimeSpan);
            }
        }

        private async Task EvaluateAsync()
        {
            var ct = _cts?.Token ?? CancellationToken.None;

            AlertMessage? next = null;

            try
            {
                if (!string.IsNullOrWhiteSpace(_url))
                {
                    next = await TryLoadFromUrlAsync(_url!, ct).ConfigureAwait(false);
                }
                else if (!string.IsNullOrWhiteSpace(_filePath))
                {
                    next = await TryLoadFromFileAsync(_filePath!, ct).ConfigureAwait(false);
                }
            }
            catch
            {
                // Conservative: on errors, treat as "no alert"
                next = null;
            }

            AlertMessage? previous;
            lock (_gate)
            {
                previous = _current;
                if (previous?.Signature == next?.Signature)
                {
                    return;
                }
                _current = next;
            }

            AlertChanged?.Invoke(this, next);
        }

        private async Task<AlertMessage?> TryLoadFromFileAsync(string path, CancellationToken ct)
        {
            if (!File.Exists(path))
            {
                return null;
            }

            var json = await File.ReadAllTextAsync(path, ct).ConfigureAwait(false);
            return ParseAlertJson(json);
        }

        private async Task<AlertMessage?> TryLoadFromUrlAsync(string url, CancellationToken ct)
        {
            if (_httpClient == null) return null;

            // Send system info to the systems endpoint (separate from alert endpoint)
            // This runs in parallel with alert fetching and doesn't block it
            if (_systemInfo != null && _baseUrl != null)
            {
                _ = TrySendSystemInfoAsync(ct);  // Fire and forget
            }

            // GET the current alert
            try
            {
                using var getReq = new HttpRequestMessage(HttpMethod.Get, url);
                using var getResp = await _httpClient.SendAsync(getReq, HttpCompletionOption.ResponseContentRead, ct).ConfigureAwait(false);

                if (!getResp.IsSuccessStatusCode)
                {
                    // Connection failed - don't update last successful connection
                    return null;
                }

                // Update last successful connection time
                _lastSuccessfulConnection = DateTime.UtcNow;

                var getJson = await getResp.Content.ReadAsStringAsync(ct).ConfigureAwait(false);
                return ParseAlertJson(getJson);
            }
            catch
            {
                // Connection failed - don't update last successful connection
                return null;
            }
        }

        private async Task TrySendSystemInfoAsync(CancellationToken ct)
        {
            if (_httpClient == null || _systemInfo == null || _baseUrl == null) return;

            try
            {
                var systemInfoJson = JsonSerializer.Serialize(new
                {
                    workstation_name = _systemInfo.WorkstationName,
                    username = _systemInfo.Username,
                    classification = _systemInfo.Classification,
                    location = _systemInfo.Location,
                    company = _systemInfo.Company,
                    background_color = _systemInfo.BackgroundColor,
                    foreground_color = _systemInfo.ForegroundColor,
                    compliance_status = _systemInfo.ComplianceStatus
                }, _jsonOptions);

                var systemsUrl = $"{_baseUrl}/api/systems";
                using var req = new HttpRequestMessage(HttpMethod.Post, systemsUrl);
                req.Content = new StringContent(systemInfoJson, Encoding.UTF8, "application/json");
                using var resp = await _httpClient.SendAsync(req, HttpCompletionOption.ResponseContentRead, ct).ConfigureAwait(false);
                
                // We don't care about the response - this is just for registration
            }
            catch
            {
                // Silently ignore system info registration failures
            }
        }

        private AlertMessage? ParseAlertJson(string? json)
        {
            if (string.IsNullOrWhiteSpace(json))
            {
                return null;
            }

            // Allow empty JSON files => no alert
            var trimmed = json.Trim();
            if (trimmed.Length == 0)
            {
                return null;
            }

            AlertPayload? payload;
            try
            {
                payload = JsonSerializer.Deserialize<AlertPayload>(trimmed, _jsonOptions);
            }
            catch
            {
                return null;
            }

            if (payload == null)
            {
                return null;
            }

            var summary = payload.Summary?.Trim() ?? string.Empty;
            var message = payload.Message?.Trim() ?? string.Empty;
            var bg = payload.BackgroundColor?.Trim() ?? string.Empty;
            var fg = payload.ForegroundColor?.Trim() ?? string.Empty;
            var levelRaw = payload.Level?.Trim() ?? string.Empty;
            var contactName = payload.AlertContactName?.Trim();
            var contactPhone = payload.AlertContactPhone?.Trim();
            var contactEmail = payload.AlertContactEmail?.Trim();
            var contactTeams = payload.AlertContactTeams?.Trim();

            if (string.IsNullOrWhiteSpace(summary) ||
                string.IsNullOrWhiteSpace(message) ||
                string.IsNullOrWhiteSpace(bg) ||
                string.IsNullOrWhiteSpace(fg) ||
                string.IsNullOrWhiteSpace(levelRaw))
            {
                return null;
            }

            var level = levelRaw.ToLowerInvariant() switch
            {
                "routine" => AlertLevel.Routine,
                "urgent" => AlertLevel.Urgent,
                "critical" => AlertLevel.Critical,
                "supercritical" => AlertLevel.SuperCritical,
                "super_critical" => AlertLevel.SuperCritical,
                "super-critical" => AlertLevel.SuperCritical,
                "systemlockdown" => AlertLevel.SystemLockdown,
                "system_lockdown" => AlertLevel.SystemLockdown,
                "system-lockdown" => AlertLevel.SystemLockdown,
                _ => (AlertLevel?)null
            };

            if (level == null)
            {
                return null;
            }

            var site = payload.Site?.Trim();
            var siteLower = string.IsNullOrWhiteSpace(site) ? null : site.ToLowerInvariant();
            var targetGroups = payload.TargetGroups;

            // Site filtering: if alert has a site, only show to workstations that have that site
            if (siteLower != null)
            {
                // Alert has a site - only show if workstation is configured with that site
                if (_workstationSites == null || !_workstationSites.Contains(siteLower))
                {
                    // Workstation has no sites configured, or doesn't include this alert's site - don't show
                    return null;
                }
            }
            
            // Group filtering: if alert has target_groups, only show to systems in those groups
            if (targetGroups != null && targetGroups.Count > 0)
            {
                // Alert has target groups - only show if system is in at least one of those groups
                if (_systemGroups == null || _systemGroups.Count == 0)
                {
                    // System has no groups configured - don't show group-targeted alerts
                    return null;
                }
                
                // Check if system is in any of the target groups
                var isInTargetGroup = targetGroups.Any(groupId => _systemGroups.Contains(groupId));
                if (!isInTargetGroup)
                {
                    // System is not in any of the target groups - don't show
                    return null;
                }
            }
            
            // If alert has neither site nor target_groups, show to everyone (backward compatible)

            var signature = ComputeSignature(trimmed);
            var alertSite = payload.Site?.Trim();
            var audioFile = payload.AudioFile?.Trim();
            
            return new AlertMessage(
                signature,
                level.Value,
                summary,
                message,
                bg,
                fg,
                string.IsNullOrWhiteSpace(contactName) ? null : contactName,
                string.IsNullOrWhiteSpace(contactPhone) ? null : contactPhone,
                string.IsNullOrWhiteSpace(contactEmail) ? null : contactEmail,
                string.IsNullOrWhiteSpace(contactTeams) ? null : contactTeams,
                string.IsNullOrWhiteSpace(alertSite) ? null : alertSite,
                string.IsNullOrWhiteSpace(audioFile) ? null : audioFile
            );
        }

        private static string ComputeSignature(string input)
        {
            var bytes = Encoding.UTF8.GetBytes(input);
            var hash = SHA256.HashData(bytes);
            var sb = new StringBuilder(hash.Length * 2);
            foreach (var b in hash)
            {
                sb.Append(b.ToString("x2"));
            }
            return sb.ToString();
        }

        public void Dispose()
        {
            Stop();
            try { _httpClient?.Dispose(); } catch { /* ignore */ }
        }
    }
}


