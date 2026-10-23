using System.Collections.Generic;
using System.Windows;
using System.Windows.Input;
using QSLauncher.Core.Services;

namespace QSLauncher.App.Views;

public sealed class CharacterChoice
{
    public CharacterChoice(YggdrasilCharacter character)
    {
        Character = character;
    }

    public YggdrasilCharacter Character { get; }

    public string Name => Character.Name;

    public string Id => Character.Id;

    public string Initial => string.IsNullOrWhiteSpace(Name)
        ? "?"
        : Name[..1].ToUpperInvariant();
}

public partial class CharacterSelectionWindow : Window
{
    public CharacterSelectionWindow(IReadOnlyList<YggdrasilCharacter> characters)
    {
        InitializeComponent();
        foreach (var character in characters)
        {
            CharacterList.Items.Add(new CharacterChoice(character));
        }

        if (CharacterList.Items.Count > 0)
        {
            CharacterList.SelectedIndex = 0;
        }
    }

    public YggdrasilCharacter? SelectedCharacter
        => (CharacterList.SelectedItem as CharacterChoice)?.Character;

    private void OnConfirmClick(object sender, RoutedEventArgs e)
    {
        if (SelectedCharacter is null)
        {
            return;
        }

        DialogResult = true;
    }

    private void OnCancelClick(object sender, RoutedEventArgs e)
        => DialogResult = false;

    private void OnListDoubleClick(object sender, MouseButtonEventArgs e)
    {
        if (SelectedCharacter is not null)
        {
            DialogResult = true;
        }
    }

    private void OnHeaderMouseDown(object sender, MouseButtonEventArgs e)
    {
        if (e.ButtonState == MouseButtonState.Pressed)
        {
            DragMove();
        }
    }
}
