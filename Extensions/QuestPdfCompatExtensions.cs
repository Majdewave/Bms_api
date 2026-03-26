using QuestPDF.Infrastructure;

namespace QuestPDF.Fluent;

public static class QuestPdfCompatExtensions
{
    public static void Layer(this IContainer container, Action<LegacyLayerScope> handler)
    {
        container.Layers(layers =>
        {
            var scope = new LegacyLayerScope(layers.PrimaryLayer(), layers.Layer());
            handler(scope);
        });
    }
}

public sealed class LegacyLayerScope
{
    private readonly IContainer _primary;
    private IContainer _overlay;

    public LegacyLayerScope(IContainer primary, IContainer overlay)
    {
        _primary = primary;
        _overlay = overlay;
    }

    public IContainer Primary() => _primary;

    public LegacyLayerScope AlignBottom()
    {
        _overlay = _overlay.AlignBottom();
        return this;
    }

    public LegacyLayerScope AlignRight()
    {
        _overlay = _overlay.AlignRight();
        return this;
    }

    public LegacyLayerScope PaddingRight(float value)
    {
        _overlay = _overlay.PaddingRight(value);
        return this;
    }

    public LegacyLayerScope PaddingBottom(float value)
    {
        _overlay = _overlay.PaddingBottom(value);
        return this;
    }

    public LegacyLayerScope Width(float value)
    {
        _overlay = _overlay.Width(value);
        return this;
    }

    public LegacyLayerScope Opacity(float value)
    {
        return this;
    }

    public LegacyLayerScope Rotate(float value)
    {
        return this;
    }

    public void Image(byte[] imageBytes)
    {
        _overlay.Image(imageBytes);
    }
}
