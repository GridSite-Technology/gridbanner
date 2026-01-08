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
        private SystemInfo? _systemInfo;  // System info to send when polling
        private string? _baseUrl;  // Base URL for downloading audio files
        
        public string? BaseUrl => _baseUrl;

        private FileSystemWatcher? _watcher;
        private Timer? _debounceTimer;
        private Timer? _urlTimer;
        private readonly HttpClient _httpClient = new()
        {
            Timeout = TimeSpan.FromSeconds(2)
        };

        private CancellationTokenSource? _cts;

        private AlertMessage? _current;

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

        public void Configure(string? alertFileLocation, string? alertUrl, TimeSpan pollInterval, string? workstationSiteNames = null, SystemInfo? systemInfo = null)
        {
            _filePath = string.IsNullOrWhiteSpace(alertFileLocation) ? null : alertFileLocation.Trim();
            _url = string.IsNullOrWhiteSpace(alertUrl) ? null : alertUrl.Trim();
            _pollInterval = pollInterval <= TimeSpan.Zero ? TimeSpan.FromSeconds(5) : pollInterval;
            _systemInfo = systemInfo;
            
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
            // Send system info if available
            if (_systemInfo != null)
            {
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

                    using var req = new HttpRequestMessage(HttpMethod.Post, url);
                    req.Content = new StringContent(systemInfoJson, Encoding.UTF8, "application/json");
                    using var resp = await _httpClient.SendAsync(req, HttpCompletionOption.ResponseContentRead, ct).ConfigureAwait(false);

                    if (!resp.IsSuccessStatusCode)
                    {
                        return null;
                    }

                    var json = await resp.Content.ReadAsStringAsync(ct).ConfigureAwait(false);
                    return ParseAlertJson(json);
                }
                catch
                {
                    // Fall back to GET if POST fails (backward compatibility)
                }
            }

            // Fallback to GET for backward compatibility or if no system info
            using var getReq = new HttpRequestMessage(HttpMethod.Get, url);
            using var getResp = await _httpClient.SendAsync(getReq, HttpCompletionOption.ResponseContentRead, ct).ConfigureAwait(false);

            if (!getResp.IsSuccessStatusCode)
            {
                return null;
            }

            var getJson = await getResp.Content.ReadAsStringAsync(ct).ConfigureAwait(false);
            return ParseAlertJson(getJson);
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
                _ => (AlertLevel?)null
            };

            if (level == null)
            {
                return null;
            }

            var site = payload.Site?.Trim();
            var siteLower = string.IsNullOrWhiteSpace(site) ? null : site.ToLowerInvariant();

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
            // If alert has no site, show to everyone (backward compatible)

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
            _httpClient.Dispose();
        }
    }
}


