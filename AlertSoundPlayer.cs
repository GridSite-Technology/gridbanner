using System;
using System.IO;
using System.Media;
using System.Net.Http;
using System.Threading.Tasks;
using System.Windows.Media;
using System.Windows.Threading;

namespace GridBanner
{
    public sealed class AlertSoundPlayer
    {
        private readonly DispatcherTimer _timer;
        private AlertMessage? _current;
        private string? _lastRoutineSignature;
        private MediaPlayer? _audioPlayer;
        private string? _currentAudioFile;
        private readonly HttpClient _httpClient;
        private readonly string _audioCacheDir;
        private string? _baseUrl;

        public AlertSoundPlayer()
        {
            _timer = new DispatcherTimer(DispatcherPriority.Background)
            {
                Interval = TimeSpan.FromSeconds(5)
            };
            _timer.Tick += (_, __) => PlaySound();
            
            _httpClient = new HttpClient();
            _audioCacheDir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "GridBanner", "audio");
            Directory.CreateDirectory(_audioCacheDir);
        }
        
        public void SetBaseUrl(string? baseUrl)
        {
            _baseUrl = baseUrl;
        }

        public void Update(AlertMessage? alert, bool dismissed)
        {
            if (alert == null)
            {
                Stop();
                return;
            }

            // If dismissed locally, stop sound for dismissable alerts.
            if (dismissed && (alert.Level == AlertLevel.Routine || alert.Level == AlertLevel.Urgent))
            {
                Stop();
                return;
            }

            // Critical/SuperCritical continue until cleared (no dismiss)
            if (alert.Level == AlertLevel.Critical || alert.Level == AlertLevel.SuperCritical)
            {
                _current = alert;
                StartLoop();
                return;
            }

            if (alert.Level == AlertLevel.Urgent)
            {
                _current = alert;
                StartLoop();
                return;
            }

            // Routine: play once per signature
            _current = alert;
            StopLoop();
            if (!string.Equals(_lastRoutineSignature, alert.Signature, StringComparison.Ordinal))
            {
                _lastRoutineSignature = alert.Signature;
                _ = PlaySoundAsync();
            }
        }

        private void StartLoop()
        {
            if (!_timer.IsEnabled)
            {
                _timer.Start();
            }
            // Also play immediately on start
            _ = PlaySoundAsync();
        }

        private void StopLoop()
        {
            if (_timer.IsEnabled)
            {
                _timer.Stop();
            }
            StopAudio();
        }

        private void PlaySound()
        {
            _ = PlaySoundAsync();
        }

        private async Task PlaySoundAsync()
        {
            if (_current == null) return;

            try
            {
                if (!string.IsNullOrWhiteSpace(_current.AudioFile))
                {
                    await PlayAudioFileAsync(_current.AudioFile);
                }
                else
                {
                    PlayBeep();
                }
            }
            catch
            {
                // Fallback to beep on error
                PlayBeep();
            }
        }

        private async Task PlayAudioFileAsync(string audioFileId)
        {
            try
            {
                // Check if we need to download or update the file
                var localPath = Path.Combine(_audioCacheDir, audioFileId);
                var needsDownload = !File.Exists(localPath);

                // Download audio file if needed
                if (needsDownload)
                {
                    if (string.IsNullOrWhiteSpace(_baseUrl))
                    {
                        // No base URL configured, fallback to beep
                        PlayBeep();
                        return;
                    }
                    
                    var downloadUrl = $"{_baseUrl}/api/audio/{Uri.EscapeDataString(audioFileId)}/download";
                    
                    try
                    {
                        var response = await _httpClient.GetAsync(downloadUrl);
                        if (response.IsSuccessStatusCode)
                        {
                            var audioData = await response.Content.ReadAsByteArrayAsync();
                            await File.WriteAllBytesAsync(localPath, audioData);
                        }
                        else
                        {
                            // Fallback to beep if download fails
                            PlayBeep();
                            return;
                        }
                    }
                    catch
                    {
                        // Fallback to beep if download fails
                        PlayBeep();
                        return;
                    }
                }

                // Stop any currently playing audio
                StopAudio();

                // Play the audio file
                _audioPlayer = new MediaPlayer();
                _currentAudioFile = localPath;
                _audioPlayer.MediaEnded += (_, __) => {
                    // For looping sounds (urgent/critical), restart if timer is still enabled
                    if (_timer.IsEnabled && _current != null)
                    {
                        _audioPlayer?.Play();
                    }
                };
                _audioPlayer.Open(new Uri(localPath, UriKind.Absolute));
                _audioPlayer.Play();
            }
            catch
            {
                // Fallback to beep on error
                PlayBeep();
            }
        }

        private void StopAudio()
        {
            try
            {
                _audioPlayer?.Stop();
                _audioPlayer?.Close();
                _audioPlayer = null;
                _currentAudioFile = null;
            }
            catch
            {
                // ignore
            }
        }

        private static void PlayBeep()
        {
            try
            {
                SystemSounds.Exclamation.Play();
            }
            catch
            {
                // ignore
            }
        }

        public void Stop()
        {
            _current = null;
            StopLoop();
            StopAudio();
        }
        
        public void Silence()
        {
            // Stop audio playback but keep the alert current
            // This allows silencing without losing the alert state
            StopLoop();
            StopAudio();
        }
    }
}
