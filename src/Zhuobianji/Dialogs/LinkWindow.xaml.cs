using System;
using System.Windows;
using Microsoft.Win32;
using Wpf.Ui.Controls;
using Zhuobianji.Core;

namespace Zhuobianji;

internal partial class LinkWindow : FluentWindow
{
    internal Uri TargetUri { get; private set; } = null!;
    internal string LinkName { get; private set; } = "";
    internal LinkWindow(NoteAppearance appearance, string address = "", string name = "")
    {
        InitializeComponent();
        NoteTheme.ApplyFluent(Resources, appearance.Dark);
        AddressBox.Text = address;
        NameBox.Text = name;
        Loaded += (_, _) => { if (address.Length == 0) AddressBox.Focus(); else NameBox.Focus(); };
    }

    private void OnSave(object sender, RoutedEventArgs e)
    {
        if (!NoteLinks.TryResolve(AddressBox.Text, out var target))
        { Status.Text = "请填写网址或完整的文件路径。"; return; }
        TargetUri = target;
        LinkName = string.IsNullOrWhiteSpace(NameBox.Text) ? NoteLinks.ShortName(target) : NameBox.Text.Trim();
        DialogResult = true;
    }

    private void OnChooseFile(object sender, RoutedEventArgs e)
    {
        var dialog = new OpenFileDialog();
        if (dialog.ShowDialog(this) == true) AddressBox.Text = dialog.FileName;
    }

    private void OnChooseFolder(object sender, RoutedEventArgs e)
    {
        var dialog = new OpenFolderDialog();
        if (dialog.ShowDialog(this) == true) AddressBox.Text = dialog.FolderName;
    }

    private void OnCancel(object sender, RoutedEventArgs e) => DialogResult = false;
}