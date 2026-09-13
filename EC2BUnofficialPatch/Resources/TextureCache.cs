using System;
using System.Collections.Generic;
using System.IO;
using UnityEngine;
using UnityObject = UnityEngine.Object;

namespace EC2BUnofficialPatch.Resources
{
    internal sealed class TextureCache : IDisposable
    {
        private readonly Dictionary<string, CachedImage> _images =
            new Dictionary<string, CachedImage>(StringComparer.OrdinalIgnoreCase);
        private readonly HashSet<string> _failedPaths =
            new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        internal bool TryGetSprite(string fullPath, out Sprite sprite)
        {
            sprite = null;
            if (string.IsNullOrWhiteSpace(fullPath) || _failedPaths.Contains(fullPath))
            {
                return false;
            }

            if (_images.TryGetValue(fullPath, out CachedImage cached))
            {
                sprite = cached.Sprite;
                return sprite != null;
            }

            try
            {
                byte[] bytes = File.ReadAllBytes(fullPath);
                Texture2D texture = new Texture2D(2, 2, TextureFormat.RGBA32, false)
                {
                    name = $"EC2B:{Path.GetFileName(fullPath)}",
                    hideFlags = HideFlags.HideAndDontSave
                };

                if (!ImageConversion.LoadImage(texture, bytes, false))
                {
                    UnityObject.Destroy(texture);
                    _failedPaths.Add(fullPath);
                    return false;
                }

                Sprite createdSprite = Sprite.Create(
                    texture,
                    new Rect(0f, 0f, texture.width, texture.height),
                    new Vector2(0.5f, 0.5f),
                    100f);
                createdSprite.name = $"EC2B:{Path.GetFileNameWithoutExtension(fullPath)}";
                createdSprite.hideFlags = HideFlags.HideAndDontSave;

                _images.Add(fullPath, new CachedImage(texture, createdSprite));
                sprite = createdSprite;
                return true;
            }
            catch
            {
                _failedPaths.Add(fullPath);
                return false;
            }
        }

        public void Dispose()
        {
            foreach (CachedImage image in _images.Values)
            {
                if (image.Sprite != null)
                {
                    UnityObject.Destroy(image.Sprite);
                }

                if (image.Texture != null)
                {
                    UnityObject.Destroy(image.Texture);
                }
            }

            _images.Clear();
            _failedPaths.Clear();
        }

        private sealed class CachedImage
        {
            internal CachedImage(Texture2D texture, Sprite sprite)
            {
                Texture = texture;
                Sprite = sprite;
            }

            internal Texture2D Texture { get; }

            internal Sprite Sprite { get; }
        }
    }
}
