using UnityEngine;
using UnityEngine.Rendering;

/// <summary>
///  シェーダー内でレンダーテクスチャを同時に読み書きするための仕組み
/// </summary>
public class DoubleRenderTexture
{
    private RenderTexture _RenderTexture0;
    private RenderTexture _RenderTexture1;
    private bool _First = true;

    public RenderTexture RenderTexture0 { get { return _RenderTexture0; } }
    public RenderTexture RenderTexture1 { get { return _RenderTexture1; } }
    public RenderTexture Active { get { return _First ? _RenderTexture0 : _RenderTexture1; } }
    public RenderTexture Inactive { get { return _First ? _RenderTexture1 : _RenderTexture0; } }
    
    public DoubleRenderTexture(int texWidth, int texHeight, int texDepth, RenderTextureFormat format = RenderTextureFormat.ARGBHalf)
    {
        _RenderTexture0 = CreateTexture(texWidth, texHeight, texDepth, format);
        _RenderTexture1 = CreateTexture(texWidth, texHeight, texDepth, format);

        //  念のためゼロクリア
        Reset();
    }

    public void Swap()
    {
        _First = !_First;
    }

    public void Reset()
    {
        Graphics.SetRenderTarget(_RenderTexture0);
        GL.Clear(false, true, new Vector4(0.0f, 0.0f, 0.0f, 0.0f));
        Graphics.SetRenderTarget(null);

        Graphics.SetRenderTarget(_RenderTexture1);
        GL.Clear(false, true, new Vector4(0.0f, 0.0f, 0.0f, 0.0f));
        Graphics.SetRenderTarget(null);
    }

    public void Release()
    {
        _RenderTexture0.Release();
        _RenderTexture1.Release();
    }

    /// <summary>
    /// RenderTextureを作成します（texDepthが0の場合は2D）
    /// </summary>
    private RenderTexture CreateTexture(int texWidth, int texHeight, int texDepth, RenderTextureFormat format)
    {
        RenderTexture rt = new RenderTexture(texWidth, texHeight, 0, format);

        //  3D
        if (texDepth > 0)
        {
            rt.dimension = TextureDimension.Tex3D;
            rt.volumeDepth = texDepth;
            rt.filterMode = FilterMode.Trilinear;
            rt.enableRandomWrite = true;
            rt.Create();
        }
        //  2D
        else
        {
            rt.filterMode = FilterMode.Bilinear;
            rt.wrapMode = TextureWrapMode.Clamp;
            rt.hideFlags = HideFlags.DontSave;
            rt.enableRandomWrite = true;
            rt.Create();
        }

        return rt;
    }
}
