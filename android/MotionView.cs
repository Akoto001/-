using System;

using Android.App;
using Android.Content;
using Android.Hardware;
using Android.Runtime;

using GeoViewer.Core;

using Camera = GeoViewer.Core.Camera;

namespace GeoViewer
{
    /// <summary>
    /// Turns the phone's orientation into camera offsets, relative to whatever pose was current when
    /// the mode was switched on. Only the delta matters, so the view never jumps: tipping the phone
    /// keeps moving the same way, and "rebase" simply re-learns the neutral pose.
    /// </summary>
    internal sealed class MotionView : Java.Lang.Object, ISensorEventListener
    {
        private readonly SensorManager _manager;
        private readonly Sensor _sensor;
        private readonly Activity _activity;

        private readonly float[] _rotation = new float[9];
        private readonly float[] _remapped = new float[9];
        private readonly float[] _values = new float[4];
        private readonly float[] _orientation = new float[3];

        private bool _hasReference;
        private double _refYaw, _refPitch, _refRoll;
        private double _lastYaw, _lastPitch, _lastRoll;
        private bool _registered;

        public MotionView(Activity activity)
        {
            _activity = activity;
            _manager = activity.GetSystemService(Context.SensorService) as SensorManager;
            if (_manager == null) return;

            _sensor = _manager.GetDefaultSensor(SensorType.RotationVector)
                      ?? _manager.GetDefaultSensor(SensorType.GameRotationVector);
        }

        public bool Available => _manager != null && _sensor != null;

        /// <summary>Reports (pitch, yaw, roll) deltas in degrees relative to the neutral pose.</summary>
        public Action<double, double, double> Changed;

        public bool Start()
        {
            if (!Available) return false;
            if (_registered) return true;
            try
            {
                _registered = _manager.RegisterListener(this, _sensor, SensorDelay.Game);
            }
            catch (Exception ex)
            {
                Diag.Write("sensor register failed", ex);
                _registered = false;
            }
            return _registered;
        }

        public void Stop()
        {
            if (!_registered) return;
            try { _manager.UnregisterListener(this); } catch { }
            _registered = false;
            _hasReference = false;
        }

        /// <summary>Take the current attitude as the new neutral pose.</summary>
        public void Rebase()
        {
            _hasReference = false;
        }

        public void OnAccuracyChanged(Sensor sensor, SensorStatus accuracy)
        {
        }

        public void OnSensorChanged(SensorEvent e)
        {
            try
            {
                if (e?.Values == null || e.Values.Count < 3 || e.Sensor?.Type != _sensor?.Type) return;

                for (int i = 0; i < _values.Length && i < e.Values.Count; i++) _values[i] = e.Values[i];

                SensorManager.GetRotationMatrixFromVector(_rotation, _values);

                // The sensor frame is fixed to the device; remap it so "up" and "right" on the sensor
                // match the way the screen is currently turned.
                Axis rx = Axis.X, ry = Axis.Y;
                switch (CurrentRotation())
                {
                    case 1: rx = Axis.Y; ry = Axis.MinusX; break;
                    case 2: rx = Axis.MinusX; ry = Axis.MinusY; break;
                    case 3: rx = Axis.MinusY; ry = Axis.X; break;
                }

                if (!SensorManager.RemapCoordinateSystem(_rotation, rx, ry, _remapped))
                {
                    Array.Copy(_rotation, _remapped, _rotation.Length);
                }

                float[] orientation = SensorManager.GetOrientation(_remapped, _orientation);
                if (orientation == null) return;

                double yaw = orientation[0] * MathUtil.Rad2Deg;
                double pitch = orientation[1] * MathUtil.Rad2Deg;
                double roll = orientation[2] * MathUtil.Rad2Deg;

                if (!_hasReference)
                {
                    _refYaw = yaw;
                    _refPitch = pitch;
                    _refRoll = roll;
                    _lastYaw = yaw;
                    _lastPitch = pitch;
                    _lastRoll = roll;
                    _hasReference = true;
                    return;
                }

                double dYaw = Camera.WrapDegrees(yaw - _refYaw);
                double dPitch = pitch - _refPitch;
                double dRoll = roll - _refRoll;

                // Cheap deadband: a phone resting on a table still jitters by a few hundredths of a
                // degree, and re-rendering for that would just burn battery.
                if (Math.Abs(dYaw - _lastYaw) < 0.25 &&
                    Math.Abs(dPitch - _lastPitch) < 0.25 &&
                    Math.Abs(dRoll - _lastRoll) < 0.25)
                {
                    return;
                }

                _lastYaw = dYaw;
                _lastPitch = dPitch;
                _lastRoll = dRoll;

                Action<double, double, double> handler = Changed;
                handler?.Invoke(dPitch, dYaw, dRoll);
            }
            catch (Exception ex)
            {
                Diag.Write("sensor read failed", ex);
            }
        }

        private int CurrentRotation()
        {
            try
            {
                return (int)_activity.WindowManager.DefaultDisplay.Rotation;
            }
            catch
            {
                return 0;
            }
        }
    }
}
