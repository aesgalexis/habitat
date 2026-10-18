using System.Runtime.InteropServices;

namespace Habitat.Services;

// Use Notepad's language-independent AddButton. Ctrl+N means Bold in Spanish
// Notepad, and keyboard shortcuts also depend on the foreground window.
internal static class NotepadTabs
{
    internal static nint OpenNewTab(nint window, Func<nint> findBlankEditor)
    {
        if (!OperatingSystem.IsWindows()) return 0;
        object? automation = null, root = null, tabCondition = null, buttonCondition = null, button = null, invoke = null;
        try
        {
            automation = Activator.CreateInstance(Type.GetTypeFromCLSID(new("FF48DBA4-60EF-4201-AA87-54103EEF594E"), true)!);
            var client = (IAutomation)automation!;
            root = client.ElementFromHandle(window);
            var element = (IElement)root;
            tabCondition = client.CreatePropertyCondition(30003, 50019); // ControlType: TabItem
            var previousTabs = TabIds(element, tabCondition);
            buttonCondition = client.CreatePropertyCondition(30011, "AddButton"); // AutomationId
            button = element.FindFirst(4, buttonCondition); // Descendants
            if (button is not IElement addButton) return 0;
            invoke = addButton.GetCurrentPattern(10000); // InvokePattern
            ((IInvoke)invoke).Invoke();

            for (var attempt = 0; attempt < 50; attempt++)
            {
                // A blank editor alone is insufficient: the existing document could
                // already be empty. Confirm that a newly created tab is selected.
                if (HasNewSelectedTab(element, tabCondition, previousTabs))
                {
                    var editor = findBlankEditor();
                    if (editor != 0) return editor;
                }
                Thread.Sleep(100);
            }
            return 0;
        }
        catch (COMException) { return 0; }
        finally
        {
            Release(invoke); Release(button); Release(buttonCondition);
            Release(tabCondition); Release(root); Release(automation);
        }
    }

    private static HashSet<string> TabIds(IElement root, object condition)
    {
        var ids = new HashSet<string>();
        VisitTabs(root, condition, tab => { ids.Add(string.Join(",", tab.GetRuntimeId())); return false; });
        return ids;
    }

    private static bool HasNewSelectedTab(IElement root, object condition, HashSet<string> previous) =>
        VisitTabs(root, condition, tab => !previous.Contains(string.Join(",", tab.GetRuntimeId())) &&
            tab.GetCurrentPropertyValue(30079) is true); // SelectionItem.IsSelected

    private static bool VisitTabs(IElement root, object condition, Func<IElement, bool> visit)
    {
        var tabs = root.FindAll(4, condition);
        try
        {
            for (var index = 0; index < tabs.Length; index++)
            {
                var tab = tabs.GetElement(index);
                try { if (visit(tab)) return true; }
                finally { Release(tab); }
            }
            return false;
        }
        finally { Release(tabs); }
    }

    private static void Release(object? value)
    {
        if (OperatingSystem.IsWindows() && value is not null && Marshal.IsComObject(value))
            Marshal.ReleaseComObject(value);
    }

    // Minimal native UI Automation declarations. Gaps preserve the vtable slots
    // in the Windows SDK's UIAutomationClient.h without adding a WPF dependency.
    [ComImport, Guid("30CBE57D-D9D0-452A-AB13-7AC5AC4825EE"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IAutomation
    {
        void _VtblGap1_3();
        IElement ElementFromHandle(nint window);
        void _VtblGap2_16();
        [return: MarshalAs(UnmanagedType.Interface)]
        object CreatePropertyCondition(int propertyId, [MarshalAs(UnmanagedType.Struct)] object value);
    }

    [ComImport, Guid("D22108AA-8AC5-49A5-837B-37BBB3D7591E"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IElement
    {
        void _VtblGap1_1();
        [return: MarshalAs(UnmanagedType.SafeArray, SafeArraySubType = VarEnum.VT_I4)]
        int[] GetRuntimeId();
        IElement? FindFirst(int scope, [MarshalAs(UnmanagedType.Interface)] object condition);
        IElementArray FindAll(int scope, [MarshalAs(UnmanagedType.Interface)] object condition);
        void _VtblGap2_3();
        [return: MarshalAs(UnmanagedType.Struct)] object GetCurrentPropertyValue(int propertyId);
        void _VtblGap3_5();
        [return: MarshalAs(UnmanagedType.IUnknown)] object GetCurrentPattern(int patternId);
    }

    [ComImport, Guid("14314595-B4BC-4055-95F2-58F2E42C9855"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IElementArray
    {
        int Length { get; }
        IElement GetElement(int index);
    }

    [ComImport, Guid("FB377FBE-8EA6-46D5-9C73-6499642D3059"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IInvoke { void Invoke(); }
}
