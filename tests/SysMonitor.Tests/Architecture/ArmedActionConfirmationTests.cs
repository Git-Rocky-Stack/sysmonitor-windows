using System.Text.RegularExpressions;
using FluentAssertions;
using SysMonitor.Tests.TestSupport;
using Xunit;

namespace SysMonitor.Tests.Architecture;

/// <summary>
/// An armed cap is the console saying "this cannot simply be undone", and the README promises nothing is destroyed
/// without asking. Browser Privacy's CLEAN carried a comment that read "In a real app, we'd show a confirmation
/// dialog here" while it deleted saved passwords; END TASK, Startup DELETE, both cleaners and Settings CLEAR ran on
/// the first press. Two rules keep that from coming back: a page that shows an armed cap asks through
/// ConsoleDialog, and a view model that can ask is handed the question by its page - an unwired
/// <c>Confirmation</c> answers no, so a forgotten wire shows up as a button that does nothing, not as a deletion.
/// </summary>
public class ArmedActionConfirmationTests
{
    private const string Views = "src/SysMonitor.App/Views";
    private const string ViewModels = "src/SysMonitor.App/ViewModels";

    private static readonly Regex ArmedCap = new(@"Armed(Icon)?CapButtonStyle", RegexOptions.Compiled);

    [Fact]
    public void EveryPageWithAnArmedCapAsksBeforeItActs()
    {
        var pages = RepoSource.FilesUnder(Views, "*.xaml")
            .Where(page => ArmedCap.IsMatch(File.ReadAllText(page)))
            .ToList();

        pages.Should().HaveCountGreaterThan(10, "this test is worthless if it cannot find the armed caps it judges");

        var silent = pages
            .Where(page =>
            {
                var code = File.Exists(page + ".cs") ? File.ReadAllText(page + ".cs") : string.Empty;
                return !code.Contains("ConsoleDialog.ConfirmAsync", StringComparison.Ordinal) &&
                       !code.Contains("ViewModel.Confirm =", StringComparison.Ordinal);
            })
            .Select(RepoSource.Relative);

        silent.Should().BeEmpty("an armed cap runs something that cannot simply be undone, and has to ask first");
    }

    [Fact]
    public void EveryViewModelThatCanAskIsGivenTheQuestionByItsPage()
    {
        var asking = RepoSource.FilesUnder(ViewModels, "*.cs")
            .Where(file => File.ReadAllText(file).Contains("public Confirmation? Confirm", StringComparison.Ordinal))
            .Select(file => Path.GetFileNameWithoutExtension(file))
            .ToList();

        asking.Should().HaveCountGreaterThan(5);

        var unwired = asking.Where(viewModel =>
        {
            var page = Path.Combine(RepoSource.Root, Views, viewModel.Replace("ViewModel", "Page") + ".xaml.cs");
            return !File.Exists(page) || !File.ReadAllText(page).Contains("ViewModel.Confirm =", StringComparison.Ordinal);
        });

        unwired.Should().BeEmpty("a view model whose page never wires Confirm can never be told yes");
    }
}
