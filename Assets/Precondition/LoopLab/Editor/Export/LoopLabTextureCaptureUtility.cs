using System;
using System.IO;
using UnityEngine;

namespace Precondition.LoopLab.Editor.Export
{
    internal static class LoopLabTextureCaptureUtility
    {
        public static Texture2D CaptureTexture(Texture source, int resolution)
        {
            if (source == null)
            {
                throw new InvalidOperationException("Loop renderer returned a null texture.");
            }

            var temporary = source as RenderTexture;
            var createdTemporary = false;

            if (temporary == null)
            {
                temporary = RenderTexture.GetTemporary(resolution, resolution, 0, RenderTextureFormat.ARGB32);
                Graphics.Blit(source, temporary);
                createdTemporary = true;
            }

            var previousActive = RenderTexture.active;
            var capture = new Texture2D(resolution, resolution, TextureFormat.RGBA32, false);

            try
            {
                RenderTexture.active = temporary;
                capture.ReadPixels(new Rect(0f, 0f, resolution, resolution), 0, 0);
                capture.Apply();
                return capture;
            }
            finally
            {
                RenderTexture.active = previousActive;

                if (createdTemporary)
                {
                    RenderTexture.ReleaseTemporary(temporary);
                }
            }
        }

        public static Texture2D LoadPng(string path)
        {
            var bytes = File.ReadAllBytes(path);
            var texture = new Texture2D(2, 2, TextureFormat.RGBA32, false);

            if (!texture.LoadImage(bytes))
            {
                UnityEngine.Object.DestroyImmediate(texture);
                throw new InvalidOperationException($"Unable to load PNG from {path}.");
            }

            return texture;
        }

        public static void WritePng(Texture2D texture, string path)
        {
            var directory = Path.GetDirectoryName(path);
            if (!string.IsNullOrEmpty(directory))
            {
                Directory.CreateDirectory(directory);
            }

            var bytes = texture.EncodeToPNG();
            File.WriteAllBytes(path, bytes);
        }
    }
}
