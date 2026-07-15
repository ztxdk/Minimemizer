using Forms = System.Windows.Forms;

namespace Minimemizer;

internal sealed class TaskViewOwnerWindow : Forms.NativeWindow, IDisposable
{
    internal TaskViewOwnerWindow()
    {
        CreateHandle(new Forms.CreateParams
        {
            Caption = "Minimemizer.TaskViewOwner",
            Style = 0,
            ExStyle = (int)(NativeMethods.WS_EX_TOOLWINDOW | NativeMethods.WS_EX_NOACTIVATE)
        });
    }

    internal nint WindowHandle => Handle;

    public void Dispose()
    {
        if (Handle != 0) DestroyHandle();
    }
}
