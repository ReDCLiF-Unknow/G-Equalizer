global using System;
global using System.Collections.Generic;
global using System.IO;
global using System.Linq;
global using System.Threading;

// Resolve WPF vs WinForms ambiguities caused by UseWindowsForms=true
global using Application = System.Windows.Application;
global using Orientation = System.Windows.Controls.Orientation;
global using HorizontalAlignment = System.Windows.HorizontalAlignment;
global using VerticalAlignment = System.Windows.VerticalAlignment;
global using Visibility = System.Windows.Visibility;
global using Window = System.Windows.Window;
global using WindowState = System.Windows.WindowState;
global using MessageBox = System.Windows.MessageBox;
global using SystemIcons = System.Drawing.SystemIcons;
