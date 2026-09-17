using System;
using UnityEngine;

namespace GamePeek
{
    /// <summary>
    /// Runtime bridge for receiving input events from the GamePeek companion app.
    /// Subscribe from any MonoBehaviour to react to phone touches and sensors.
    /// <para>
    /// Works with <b>both</b> input backends — the events here fire regardless of
    /// whether the project uses the Legacy Input Manager, the new Input System
    /// package, or Unity's "Both" mode. Touch is additionally injected into the
    /// active backend(s) (legacy <c>Input.GetTouch</c> and/or a virtual Input
    /// System <c>Touchscreen</c>); gyroscope and accelerometer data is
    /// additionally injected as virtual Input System sensor devices when
    /// <c>com.unity.inputsystem</c> is installed. The Legacy Input Manager has no
    /// injection hook for sensors — <c>Input.gyro</c> / <c>Input.acceleration</c>
    /// always read zero in the editor — so in legacy-only projects
    /// <see cref="OnGyro"/> / <see cref="OnAccel"/> are the way to consume
    /// sensor data.
    /// </para>
    /// <para>
    /// All events are invoked on the Unity main thread. Sensor streaming
    /// (gyroscope / accelerometer) is a Pro feature of the GamePeek app.
    /// </para>
    /// </summary>
    public static class GamePeekInput
    {
        /// <summary>
        /// Fired on the main thread whenever a touch event arrives from the phone.
        /// Argument is the normalised position: x=0 left, x=1 right, y=0 top, y=1 bottom.
        /// </summary>
        public static Action<Vector2> OnTouch;

        /// <summary>
        /// Fired on the main thread for every touch event with full details.
        /// Args: fingerId, phase ("began"|"moved"|"ended"|"canceled"), normalised position.
        /// </summary>
        public static Action<int, string, Vector2> OnTouchDetailed;

        /// <summary>
        /// Fired on the main thread for every gyroscope sample from the phone.
        /// Value is the rotation rate in radians/second around the device's
        /// x/y/z axes — the same units and axes as legacy
        /// <c>Input.gyro.rotationRate</c> and the Input System's
        /// <c>Gyroscope.angularVelocity</c>.
        /// </summary>
        public static Action<Vector3> OnGyro;

        /// <summary>
        /// Fired on the main thread for every accelerometer sample from the phone.
        /// Value is the acceleration including gravity in g-multiples using
        /// Unity's sign convention (device flat on a table, screen up ⇒
        /// (0, 0, -1)) — a drop-in replacement for legacy <c>Input.acceleration</c>.
        /// </summary>
        public static Action<Vector3> OnAccel;
    }
}
