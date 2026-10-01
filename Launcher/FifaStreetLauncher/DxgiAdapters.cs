using System.Runtime.InteropServices;

namespace FifaStreetLauncher;

internal static class DxgiAdapters
{
    internal sealed record Adapter(uint Index, string Name, ulong DedicatedMemory);

    [DllImport("dxgi.dll", ExactSpelling = true)]
    private static extern int CreateDXGIFactory1(ref Guid iid, out IntPtr factory);

    [UnmanagedFunctionPointer(CallingConvention.StdCall)]
    private delegate int EnumAdapters1(IntPtr factory, uint index, out IntPtr adapter);

    [UnmanagedFunctionPointer(CallingConvention.StdCall)]
    private delegate int GetDesc1(IntPtr adapter, out AdapterDescription description);

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct AdapterDescription
    {
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 128)]
        public string Description;
        public uint VendorId, DeviceId, SubSysId, Revision;
        public UIntPtr DedicatedVideoMemory, DedicatedSystemMemory, SharedSystemMemory;
        public uint LuidLow;
        public int LuidHigh;
        public uint Flags;
    }

    private static T Method<T>(IntPtr instance, int slot) where T : Delegate =>
        Marshal.GetDelegateForFunctionPointer<T>(
            Marshal.ReadIntPtr(Marshal.ReadIntPtr(instance), slot * IntPtr.Size));

    internal static List<Adapter> Enumerate()
    {
        var result = new List<Adapter>();
        var iid = new Guid("770aae78-f26f-4dba-a829-253c83d1b387");
        Marshal.ThrowExceptionForHR(CreateDXGIFactory1(ref iid, out var factory));
        try
        {
            var enumerate = Method<EnumAdapters1>(factory, 12);
            for (uint index = 0; ; index++)
            {
                int status = enumerate(factory, index, out var adapter);
                if (status == unchecked((int)0x887A0002)) // DXGI_ERROR_NOT_FOUND
                    break;
                Marshal.ThrowExceptionForHR(status);
                try
                {
                    Marshal.ThrowExceptionForHR(Method<GetDesc1>(adapter, 10)(adapter, out var description));
                    if ((description.Flags & 2) == 0) // DXGI_ADAPTER_FLAG_SOFTWARE
                        result.Add(new Adapter(index, description.Description,
                            description.DedicatedVideoMemory.ToUInt64()));
                }
                finally
                {
                    Marshal.Release(adapter);
                }
            }
        }
        finally
        {
            Marshal.Release(factory);
        }
        return result;
    }
}
