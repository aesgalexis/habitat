namespace Habitat.Features.Contacts;

// Respect an intentional switch to another app; only Phone Link activation is
// redirected while the operation still belongs to HABITAT.
internal sealed class PhoneLinkFocusKeeper(nint owner, Func<nint> foreground,
    Func<nint, bool> isPhoneLink, Action<nint> restore)
{
    private bool _ownsFocus = owner != 0 && foreground() == owner;
    internal void ForegroundChanged()
    {
        var current = foreground();
        if (current == owner) { _ownsFocus = true; return; }
        if (current == 0) return;
        if (_ownsFocus && isPhoneLink(current)) restore(owner);
        else _ownsFocus = false;
    }
}
