using System;
using System.IO;
using System.Windows;
using P2PLocalFileShareServer.Models;
using P2PLocalFileShareServer.ViewModels;

namespace P2PLocalFileShareServer
{
    public partial class MainWindow : Window
    {
        private readonly MainViewModel _viewModel;

        public MainWindow()
        {
            InitializeComponent();
            _viewModel = new MainViewModel();
            DataContext = _viewModel;
        }

        private void Window_DragOver(object sender, DragEventArgs e)
        {
            if (e.Data.GetDataPresent(DataFormats.FileDrop))
            {
                e.Effects = DragDropEffects.Copy;
            }
            else
            {
                e.Effects = DragDropEffects.None;
            }
            e.Handled = true;
        }

        private void Window_Drop(object sender, DragEventArgs e)
        {
            if (e.Data.GetDataPresent(DataFormats.FileDrop))
            {
                string[] files = (string[])e.Data.GetData(DataFormats.FileDrop);
                if (files != null)
                {
                    foreach (string file in files)
                    {
                        if (File.Exists(file))
                        {
                            var info = new FileInfo(file);
                            _viewModel.SharedFiles.Add(new SharedFileItem
                            {
                                FileName = info.Name,
                                FilePath = info.FullName,
                                FileSizeBytes = info.Length
                            });
                        }
                    }
                }
            }
        }
    }
}
