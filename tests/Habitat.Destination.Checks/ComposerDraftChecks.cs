using Habitat.Services;

internal static class ComposerDraftChecks
{
    internal static void Run(Action<bool, string> check)
    {
        const string original = "  café ☕\nsecond line  ";
        var privateDrafts = new ComposerDrafts();
        privateDrafts.Edit(ComposerDestination.OpenAI, "Unrelated OpenAI draft");
        privateDrafts.Isolate("contact:a"); privateDrafts.Isolate("contact:b");
        check(privateDrafts.Text("contact:a") == "" && privateDrafts.Text("contact:b") == "", "Contact editors never inherit shared drafts");
        privateDrafts.Edit("contact:a", original);
        check(privateDrafts.Text("contact:b") == "" && privateDrafts.Text(ComposerDestination.OpenAI) == "Unrelated OpenAI draft", "Private recipient text cannot leak into another destination");
        var privateSubmission = privateDrafts.Capture("contact:a");
        privateDrafts.Accept(privateSubmission); privateDrafts.Clear(privateSubmission);
        privateDrafts.Edit("contact:a", "A second private message");
        check(privateDrafts.Text("contact:b") == "" && privateDrafts.Text(ComposerDestination.OpenAI) == "Unrelated OpenAI draft", "Cleared contacts remain isolated when composing another message");
        var destinations = ComposerDestinations.Ordered.ToArray();
        var drafts = new ComposerDrafts();
        drafts.Edit(ComposerDestination.OpenAI, original);
        check(destinations.All(destination => drafts.Text(destination) == original),
            "Unsubmitted text travels unchanged through every destination");
        drafts.Capture(ComposerDestination.Terminal);
        check(destinations.All(destination => drafts.Text(destination) == original),
            "A canceled confirmation or rejected send leaves the shared draft intact");

        foreach (var destination in destinations)
        {
            var state = new ComposerDrafts();
            state.Edit(destination, original);
            var submission = state.Capture(destination);
            state.Accept(submission);
            check(state.Text(destination) == original && destinations.Where(other => other != destination)
                .All(other => state.Text(other) == ""),
                $"An accepted {destination} action keeps its text only in that destination");
            state.Clear(submission);
            check(destinations.All(other => state.Text(other) == ""),
                $"Successful {destination} delivery clears the submitted draft");
        }

        var translation = drafts.Capture(ComposerDestination.Translate);
        drafts.Accept(translation);
        drafts.Edit(ComposerDestination.Translate, "Edited translation source");
        check(drafts.Text(ComposerDestination.Translate) == "Edited translation source" &&
            drafts.Text(ComposerDestination.Google) == "",
            "Editing a used translation stays local to Translate");
        drafts.Edit(ComposerDestination.Google, "New pending draft");
        check(drafts.Text(ComposerDestination.YouTube) == "New pending draft" &&
            drafts.Text(ComposerDestination.OpenAI) == "New pending draft" &&
            drafts.Text(ComposerDestination.Translate) == "Edited translation source",
            "A new shared draft travels without replacing a destination's own text");
        drafts.Edit(ComposerDestination.Translate, "");
        check(drafts.Text(ComposerDestination.Google) == "New pending draft" && drafts.Text(ComposerDestination.Translate) == "",
            "Clearing local text preserves another pending draft");

        drafts = new ComposerDrafts();
        drafts.Edit(ComposerDestination.Translate, original);
        drafts.Accept(drafts.Capture(ComposerDestination.Translate));
        drafts.Edit(ComposerDestination.Translate, "");
        drafts.Edit(ComposerDestination.Translate, "Fresh text");
        check(destinations.All(destination => drafts.Text(destination) == "Fresh text"),
            "Clearing used text starts a fresh shared draft when no other draft is waiting");

        drafts = new ComposerDrafts();
        drafts.Edit(ComposerDestination.Google, original);
        var search = drafts.Capture(ComposerDestination.Google);
        drafts.Accept(search);
        drafts.Edit(ComposerDestination.YouTube, original);
        drafts.Clear(search);
        check(drafts.Text(ComposerDestination.YouTube) == original && drafts.Text(ComposerDestination.OpenAI) == original,
            "A pending search cannot erase a new shared draft with identical text");

        drafts = new ComposerDrafts();
        drafts.Edit(ComposerDestination.Google, original);
        search = drafts.Capture(ComposerDestination.Google);
        drafts.Accept(search);
        drafts.Edit(ComposerDestination.Google, "Changed while opening");
        drafts.Edit(ComposerDestination.Google, original);
        drafts.Clear(search);
        check(drafts.Text(ComposerDestination.Google) == original && drafts.Text(ComposerDestination.YouTube) == "",
            "A pending search cannot erase a newer local revision with identical text");

        drafts = new ComposerDrafts();
        drafts.Edit(ComposerDestination.Terminal, original);
        var command = drafts.Capture(ComposerDestination.Terminal);
        drafts.Edit(ComposerDestination.Terminal, "New text during confirmation");
        drafts.Accept(command);
        drafts.Clear(command);
        check(destinations.All(destination => drafts.Text(destination) == "New text during confirmation"),
            "Accepting an older confirmation cannot consume a newer draft");
    }
}
