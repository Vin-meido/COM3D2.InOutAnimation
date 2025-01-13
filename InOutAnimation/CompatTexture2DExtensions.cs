using System;
using System.Collections.Generic;
using System.Reflection;
using System.Linq;
using System.Text;
using UnityEngine;
using System.ComponentModel;

namespace COM3D2.InOutAnimation.Plugin
{
    static class CompatTexture2DExtensions
    {
        static MethodInfo ImageConversionLoadImage;
        static MethodInfo Texture2DLoadImage;

        static CompatTexture2DExtensions()
        {
            Texture2DLoadImage = typeof(Texture2D).GetMethod("LoadImage", new Type[] { typeof(byte[]) });

            if (Texture2DLoadImage == null)
            {
                // find assembly named UnityEngine.ImageConversionModule
                // then search for ImageConversion class in that assembly:
                // ImageConversion.LoadImage
                // public static bool LoadImage(Texture2D tex, byte[] data, bool markNonReadable);
                var imageConversionModule = AppDomain.CurrentDomain.GetAssemblies().FirstOrDefault(a => a.GetName().Name == "UnityEngine.ImageConversionModule");
                if (imageConversionModule == null)
                {
                    throw new Exception("Cannot find UnityEngine.ImageConversionModule assembly");
                }

                var imageConversionType = imageConversionModule.GetType("UnityEngine.ImageConversion");
                if (imageConversionType == null)
                {
                    throw new Exception("Cannot find UnityEngine.ImageConversion type");
                }

                ImageConversionLoadImage = imageConversionType.GetMethod("LoadImage", new Type[] { typeof(Texture2D), typeof(byte[]), typeof(bool) });
                if (imageConversionType != null)
                {
                    ImageConversionLoadImage = imageConversionType.GetMethod("LoadImage", new Type[] { typeof(Texture2D), typeof(byte[]), typeof(bool) });
                }
            }
        }

        public static bool LoadImageCompat(this Texture2D texture, byte[] data)
        {
            if (ImageConversionLoadImage != null)
            {
                return (bool)ImageConversionLoadImage.Invoke(null, new object[] { texture, data, false });
            }
            else
            {
                return (bool)Texture2DLoadImage.Invoke(texture, new object[] { data });
            }
        }
    }
}
