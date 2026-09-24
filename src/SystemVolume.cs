using System;
using System.Runtime.InteropServices;

namespace FreeIsland
{
    internal static class SystemVolume
    {
        [ComImport, Guid("BCDE0395-E52F-467C-8E3D-C4579291692E")] private class Enumerator { }
        [ComImport, Guid("A95664D2-9614-4F35-A746-DE8DB63617E6"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
        private interface IDevices
        {
            [PreserveSig] int EnumAudioEndpoints(int flow, int state, out IntPtr devices);
            [PreserveSig] int GetDefaultAudioEndpoint(int flow, int role, out IDevice device);
        }
        [ComImport, Guid("D666063F-1587-4E43-81F1-B948E807363F"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
        private interface IDevice { [PreserveSig] int Activate(ref Guid id, int context, IntPtr parameters, [MarshalAs(UnmanagedType.IUnknown)] out object result); }
        [ComImport, Guid("5CDF2C82-841E-4546-9722-0CF74078229A"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
        private interface IVolume
        {
            [PreserveSig] int RegisterControlChangeNotify(IntPtr callback);
            [PreserveSig] int UnregisterControlChangeNotify(IntPtr callback);
            [PreserveSig] int GetChannelCount(out uint count);
            [PreserveSig] int SetMasterVolumeLevel(float value, ref Guid context);
            [PreserveSig] int SetMasterVolumeLevelScalar(float value, ref Guid context);
            [PreserveSig] int GetMasterVolumeLevel(out float value);
            [PreserveSig] int GetMasterVolumeLevelScalar(out float value);
        }
        public static bool TryGet(out int percent) { int value = 0; bool ok = WithEndpoint(delegate(IVolume v) { float current; Marshal.ThrowExceptionForHR(v.GetMasterVolumeLevelScalar(out current)); value = (int)Math.Round(current * 100); }); percent = value; return ok; }
        public static bool TrySet(int percent) { return WithEndpoint(delegate(IVolume v) { Guid id = Guid.Empty; Marshal.ThrowExceptionForHR(v.SetMasterVolumeLevelScalar(Math.Max(0, Math.Min(100, percent)) / 100f, ref id)); }); }
        private static bool WithEndpoint(Action<IVolume> action)
        {
            object enumerator = null, endpoint = null; IDevice device = null;
            try
            {
                enumerator = new Enumerator(); Marshal.ThrowExceptionForHR(((IDevices)enumerator).GetDefaultAudioEndpoint(0, 1, out device));
                Guid id = typeof(IVolume).GUID; Marshal.ThrowExceptionForHR(device.Activate(ref id, 23, IntPtr.Zero, out endpoint)); action((IVolume)endpoint); return true;
            }
            catch { return false; }
            finally { if (endpoint != null) Marshal.ReleaseComObject(endpoint); if (device != null) Marshal.ReleaseComObject(device); if (enumerator != null) Marshal.ReleaseComObject(enumerator); }
        }
        [DllImport("user32.dll")] private static extern void keybd_event(byte key, byte scan, uint flags, UIntPtr extra);
        public static void ToggleMedia() { keybd_event(0xB3, 0, 0, UIntPtr.Zero); keybd_event(0xB3, 0, 2, UIntPtr.Zero); }
    }
}
