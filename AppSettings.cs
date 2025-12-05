using System;
using System.Drawing;
using System.Windows.Forms;

namespace CpuTempApp
{
    public static class AppSettings
    {
        private static bool _showCpu = false;
        private static bool _showGpu = false;
        private static Color _textColor = Color.Cyan;  // Default color is cyan

        public static bool ShowCpu
        {
            get => _showCpu;
            set
            {
                if (_showCpu == value) return;
                _showCpu = value;
                RaiseSettingsChanged();
            }
        }

        public static bool ShowGpu
        {
            get => _showGpu;
            set
            {
                if (_showGpu == value) return;
                _showGpu = value;
                RaiseSettingsChanged();
            }
        }

        public static Color TextColor
        {
            get => _textColor;
            set
            {
                if (_textColor == value) return;
                _textColor = value;
                RaiseSettingsChanged();
            }
        }

        public static event Action? SettingsChanged;
        public static void RaiseSettingsChanged() => SettingsChanged?.Invoke();

        // Optional: allow selecting an exact sensor name to use for display
        private static string _selectedCpuSensor = string.Empty;
        private static string _selectedGpuSensor = string.Empty;
        private static bool _cpuSensorIsDistanceToTjMax = false;
        private static int _cpuTjMax = 100;

        public static string SelectedCpuSensor
        {
            get => _selectedCpuSensor;
            set
            {
                if (_selectedCpuSensor == value) return;
                _selectedCpuSensor = value ?? string.Empty;
                RaiseSettingsChanged();
            }
        }

        public static string SelectedGpuSensor
        {
            get => _selectedGpuSensor;
            set
            {
                if (_selectedGpuSensor == value) return;
                _selectedGpuSensor = value ?? string.Empty;
                RaiseSettingsChanged();
            }
        }

        public static bool CpuSensorIsDistanceToTjMax
        {
            get => _cpuSensorIsDistanceToTjMax;
            set
            {
                if (_cpuSensorIsDistanceToTjMax == value) return;
                _cpuSensorIsDistanceToTjMax = value;
                RaiseSettingsChanged();
            }
        }

        public static int CpuTjMax
        {
            get => _cpuTjMax;
            set
            {
                if (_cpuTjMax == value) return;
                _cpuTjMax = value;
                RaiseSettingsChanged();
            }
        }
    }
}