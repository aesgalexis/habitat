using System.Runtime.InteropServices;
using System.Runtime.Versioning;

namespace Habitat.Services;

[SupportedOSPlatform("windows")]
internal static class WindowsApps
{
    // AppsFolder includes both desktop and Microsoft Store applications.
    public static Task<IReadOnlyList<WindowsAppDestination>> DiscoverAsync()
    {
        var completion = new TaskCompletionSource<IReadOnlyList<WindowsAppDestination>>(TaskCreationOptions.RunContinuationsAsynchronously);
        var thread = new Thread(() =>
        {
            object? shell = null, folder = null, items = null;
            try
            {
                shell = Activator.CreateInstance(Type.GetTypeFromProgID("Shell.Application")!);
                folder = ((dynamic)shell!).NameSpace("shell:AppsFolder");
                items = ((dynamic)folder!).Items();
                var apps = new List<WindowsAppDestination>();
                for (var index = 0; index < (int)((dynamic)items).Count; index++)
                {
                    object? item = null;
                    try
                    {
                        item = ((dynamic)items).Item(index);
                        var name = (string)((dynamic)item!).Name;
                        var target = (string)((dynamic)item).Path;
                        var app = new WindowsAppDestination(WindowsAppDestination.NewId(), name, target,
                            Packaged: !Path.IsPathFullyQualified(target));
                        if (app.IsValid) apps.Add(app);
                    }
                    finally { Release(item); }
                }
                completion.SetResult(apps.DistinctBy(app => app.Target, StringComparer.OrdinalIgnoreCase)
                    .OrderBy(app => app.Name, StringComparer.CurrentCultureIgnoreCase).ToArray());
            }
            catch (Exception exception) { completion.SetException(exception); }
            finally { Release(items); Release(folder); Release(shell); }
        }) { IsBackground = true, Name = "HABITAT installed applications" };
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        return completion.Task;
    }

    private static void Release(object? value)
    {
        if (value is not null && Marshal.IsComObject(value)) Marshal.ReleaseComObject(value);
    }
}
