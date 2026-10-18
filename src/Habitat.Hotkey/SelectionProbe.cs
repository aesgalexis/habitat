using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using Habitat.Activation;

namespace Habitat.Hotkey;

// Read-only accessibility inspection is necessary: several editors copy a whole
// line on Ctrl+C with no selection. A clipboard sequence check alone cannot tell.
[SupportedOSPlatform("windows")]
internal static class SelectionProbe
{
    public static string? SelectedHash(nint source, Action<string, object?>? diagnostic = null, bool inspectBackground = false)
    {
        var objects = new List<object>();
        T Keep<T>(T value) where T : class { objects.Add(value); return value; }
        try
        {
            if (!inspectBackground && Native.GetForegroundWindow() != source) return null;
            var client = Keep((IAutomation)Activator.CreateInstance(Type.GetTypeFromCLSID(new("FF48DBA4-60EF-4201-AA87-54103EEF594E"), true)!)!);
            var root = Keep(client.ElementFromHandle(source));
            var element = Keep(client.GetFocusedElement());
            diagnostic?.Invoke("FocusedType", element.GetCurrentPropertyValue(30003));
            diagnostic?.Invoke("FocusedTextPattern", element.GetCurrentPropertyValue(30040));
            diagnostic?.Invoke("FocusedProcess", element.GetCurrentPropertyValue(30002));
            diagnostic?.Invoke("WindowProcess", root.GetCurrentPropertyValue(30002));
            if (!inspectBackground && element.GetCurrentPropertyValue(30019) is true) return null;

            // Validate membership in this HWND's accessibility subtree, not a PID:
            // Chromium accessibility providers may live in a renderer process.
            var focusedCondition = Keep(client.CreatePropertyCondition(30008, true)); // HasKeyboardFocus
            var focusedProviders = Keep(root.FindAll(4, focusedCondition));
            if (focusedProviders.Length > 64) return null;
            var focusedId = element.GetRuntimeId();
            var belongs = root.GetRuntimeId().SequenceEqual(focusedId);
            for (var index = 0; index < focusedProviders.Length && !belongs; index++)
                belongs = Keep(focusedProviders.GetElement(index)).GetRuntimeId().SequenceEqual(focusedId);
            diagnostic?.Invoke("FocusInsideWindow", belongs);
            if (!belongs && !inspectBackground) return null;

            var candidates = new List<string[]>();
            void Read(IElement provider)
            {
                if (provider.GetCurrentPropertyValue(30019) is true || provider.GetCurrentPropertyValue(30022) is true ||
                    provider.GetCurrentPropertyValue(30040) is not true) return;
                try
                {
                    var pattern = Keep((IText)provider.GetCurrentPattern(10014));
                    var selection = Keep(pattern.GetSelection());
                    if (selection.Length > 1) { candidates.Add(["", ""]); return; }
                    candidates.Add(selection.Length == 0 ? [] :
                        [Keep(selection.GetElement(0)).GetText(ActivationChannel.MaxTextLength + 1)]);
                }
                catch (COMException) { } // A replaced or unsupported provider is not evidence.
            }
            if (belongs) Read(element);
            Read(root);
            var documentCondition = Keep(client.CreatePropertyCondition(30003, 50030)); // ControlType.Document
            var documents = Keep(root.FindAll(4, documentCondition));
            diagnostic?.Invoke("DocumentProviders", documents.Length);
            var providerProcesses = new List<object>();
            if (documents.Length > 60) return null;
            for (var index = 0; index < documents.Length; index++)
            {
                var document = Keep(documents.GetElement(index));
                providerProcesses.Add(new { Process = document.GetCurrentPropertyValue(30002),
                    TextPattern = document.GetCurrentPropertyValue(30040), Offscreen = document.GetCurrentPropertyValue(30022) });
                Read(document);
            }
            diagnostic?.Invoke("DocumentProperties", providerProcesses);
            diagnostic?.Invoke("SelectionLengths", candidates.Select(ranges => ranges.Select(text => text.Length).ToArray()).ToArray());
            return Native.GetForegroundWindow() == source ? SelectionResolver.Resolve(belongs, false, candidates) : null;
        }
        catch (Exception exception) { diagnostic?.Invoke("Failure", exception.GetType().Name); return null; }
        finally
        {
            for (var index = objects.Count - 1; index >= 0; index--)
                if (Marshal.IsComObject(objects[index])) Marshal.ReleaseComObject(objects[index]);
        }
    }
    [ComImport, Guid("30CBE57D-D9D0-452A-AB13-7AC5AC4825EE"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IAutomation
    {
        void _VtblGap1_3(); IElement ElementFromHandle(nint window);
        void _VtblGap2_1(); IElement GetFocusedElement();
        void _VtblGap3_14();
        [return: MarshalAs(UnmanagedType.Interface)] object CreatePropertyCondition(int property, [MarshalAs(UnmanagedType.Struct)] object value);
    }
    [ComImport, Guid("D22108AA-8AC5-49A5-837B-37BBB3D7591E"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IElement
    {
        void _VtblGap1_1();
        [return: MarshalAs(UnmanagedType.SafeArray, SafeArraySubType = VarEnum.VT_I4)] int[] GetRuntimeId();
        IElement? FindFirst(int scope, [MarshalAs(UnmanagedType.Interface)] object condition);
        IElementArray FindAll(int scope, [MarshalAs(UnmanagedType.Interface)] object condition);
        void _VtblGap2_3();
        [return: MarshalAs(UnmanagedType.Struct)] object GetCurrentPropertyValue(int propertyId);
        void _VtblGap3_5();
        [return: MarshalAs(UnmanagedType.IUnknown)] object GetCurrentPattern(int patternId);
    }
    [ComImport, Guid("14314595-B4BC-4055-95F2-58F2E42C9855"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IElementArray { int Length { get; } IElement GetElement(int index); }
    [ComImport, Guid("32EBA289-3583-42C9-9C59-3B6D9A1E9B6A"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IText { void _VtblGap1_2(); IRangeArray GetSelection(); }
    [ComImport, Guid("CE4AE76A-E717-4C98-81EA-47371D028EB6"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IRangeArray { int Length { get; } IRange GetElement(int index); }
    [ComImport, Guid("A543CC6A-F4AE-494B-8239-C814481187A8"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IRange
    { void _VtblGap1_9(); [return: MarshalAs(UnmanagedType.BStr)] string GetText(int maxLength); }
}
