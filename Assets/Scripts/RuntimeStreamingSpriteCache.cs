using System;
using System.Collections.Generic;
using System.IO;
using UnityEngine;

public static class RuntimeStreamingSpriteCache
{
    struct CacheEntry
    {
        public long Length;
        public long LastWriteTicksUtc;
        public Sprite Sprite;
    }

    static readonly Dictionary<string, CacheEntry> _cache =
        new Dictionary<string, CacheEntry>(StringComparer.OrdinalIgnoreCase);

    public static bool TryGetOrCreateSprite(string fullPath, out Sprite sprite, out string error)
    {
        sprite = null;
        error = null;

        if (string.IsNullOrEmpty(fullPath))
        {
            error = "Path is empty.";
            return false;
        }

        FileInfo fi;
        try
        {
            fi = new FileInfo(fullPath);
            if (!fi.Exists)
            {
                error = "File does not exist: " + fullPath;
                return false;
            }
        }
        catch (Exception e)
        {
            error = "Could not stat file: " + e.Message;
            return false;
        }

        if (_cache.TryGetValue(fullPath, out CacheEntry cached))
        {
            if (cached.Length == fi.Length &&
                cached.LastWriteTicksUtc == fi.LastWriteTimeUtc.Ticks &&
                cached.Sprite != null)
            {
                sprite = cached.Sprite;
                return true;
            }

            DestroyCachedSprite(cached.Sprite);
        }

        byte[] bytes;
        try { bytes = File.ReadAllBytes(fullPath); }
        catch (Exception e)
        {
            error = "Could not read image: " + e.Message;
            return false;
        }

        var tex = new Texture2D(2, 2, TextureFormat.RGBA32, false);
        if (!tex.LoadImage(bytes))
        {
            UnityEngine.Object.Destroy(tex);
            error = "Failed to decode image: " + fullPath;
            return false;
        }

        // Runtime normalization for newly added StreamingAssets textures.
        tex.filterMode = FilterMode.Bilinear;
        tex.wrapMode = TextureWrapMode.Clamp;
        tex.anisoLevel = 1;

        var created = Sprite.Create(
            tex,
            new Rect(0, 0, tex.width, tex.height),
            new Vector2(0f, 1f),
            100f,
            0u,
            SpriteMeshType.FullRect);
        created.name = Path.GetFileNameWithoutExtension(fullPath);

        _cache[fullPath] = new CacheEntry
        {
            Length = fi.Length,
            LastWriteTicksUtc = fi.LastWriteTimeUtc.Ticks,
            Sprite = created
        };

        sprite = created;
        return true;
    }

    static void DestroyCachedSprite(Sprite s)
    {
        if (s == null) return;
        var tex = s.texture;
        UnityEngine.Object.Destroy(s);
        if (tex != null) UnityEngine.Object.Destroy(tex);
    }
}
