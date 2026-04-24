using Microsoft.Maui.Controls;

namespace MortonPlazmer.Controls
{
    public class CustomWebView : WebView
    {
        public static new readonly BindableProperty UserAgentProperty =
            BindableProperty.Create(
                nameof(UserAgent),
                typeof(string),
                typeof(CustomWebView),
                default(string));

        public new string UserAgent
        {
            get => (string)GetValue(UserAgentProperty);
            set => SetValue(UserAgentProperty, value);
        }
    }
}
