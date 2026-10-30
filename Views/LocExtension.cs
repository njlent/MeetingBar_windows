using System;
using System.ComponentModel;
using System.Globalization;
using System.Windows;
using System.Windows.Data;
using System.Windows.Markup;
using MeetingBar.Localization;

namespace MeetingBar.Windows.Views
{
    /// <summary>XAML text that follows the MeetingBar language, for example Text="{views:Loc 'Join meeting'}".</summary>
    [MarkupExtensionReturnType(typeof(object))]
    public sealed class LocExtension : MarkupExtension
    {
        public LocExtension() { }
        public LocExtension(string text) { Text = text; }

        [ConstructorArgument("text")]
        public string Text { get; set; }

        public override object ProvideValue(IServiceProvider serviceProvider) => new Binding(nameof(LanguageSource.Revision))
        {
            Source = LanguageSource.Instance,
            Mode = BindingMode.OneWay,
            Converter = TranslateConverter.Instance,
            ConverterParameter = Text
        }.ProvideValue(serviceProvider);

        private sealed class TranslateConverter : IValueConverter
        {
            public static readonly TranslateConverter Instance = new TranslateConverter();
            public object Convert(object value, Type targetType, object parameter, CultureInfo culture) => Localizer.T(parameter as string ?? "");
            public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture) => Binding.DoNothing;
        }
    }

    /// <summary>Notifies bindings and windows when the MeetingBar language changes.</summary>
    public sealed class LanguageSource : INotifyPropertyChanged
    {
        public static LanguageSource Instance { get; } = new LanguageSource();

        private LanguageSource()
        {
            Localizer.LanguageChanged += (s, e) =>
            {
                Revision++;
                PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(Revision)));
                foreach (Window window in Application.Current.Windows) window.Language = XmlLanguage;
            };
        }

        public int Revision { get; private set; }
        public event PropertyChangedEventHandler PropertyChanged;

        private static XmlLanguage XmlLanguage => XmlLanguage.GetLanguage(Localizer.Culture.IetfLanguageTag);

        /// <summary>WPF picks CJK font fallbacks from xml:lang, so every window uses the UI language.</summary>
        public static void Initialize()
        {
            _ = Instance;
            EventManager.RegisterClassHandler(typeof(Window), FrameworkElement.LoadedEvent, new RoutedEventHandler((s, e) => ((Window)s).Language = XmlLanguage));
        }
    }
}
