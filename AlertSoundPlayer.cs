using System;
using System.Media;
using System.Windows.Threading;

namespace GridBanner
{
    public sealed class AlertSoundPlayer
    {
        private readonly DispatcherTimer _timer;
        private AlertMessage? _current;
        private string? _lastRoutineSignature;

        public AlertSoundPlayer()
        {
            _timer = new DispatcherTimer(DispatcherPriority.Background)
            {
                Interval = TimeSpan.FromSeconds(5)
            };
            _timer.Tick += (_, __) => PlayBeep();
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
                PlayBeep();
            }
        }

        private void StartLoop()
        {
            if (!_timer.IsEnabled)
            {
                _timer.Start();
            }
            // Also beep immediately on start
            PlayBeep();
        }

        private void StopLoop()
        {
            if (_timer.IsEnabled)
            {
                _timer.Stop();
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
        }
    }
}


