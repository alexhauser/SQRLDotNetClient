﻿using SQRLDotNetClientUI.Models;
using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using System.Text;

namespace SQRLDotNetClientUI.Platform
{
    /// <summary>
    /// Provides access to platform-specific implementations.
    /// </summary>
    public static class Implementation
    {
        /// <summary>
        /// Returns a <c>Type</c> that represents the platform-specific implementation for 
        /// the given type <typeparamref name="T"/>.
        /// </summary>
        /// <typeparam name="T">The <c>Type</c> (mostly an interface type) to get a platform-
        /// specific implementation for.</typeparam>
        /// <returns></returns>
        public static Type ForType<T>()
        {
            if (typeof(T) == typeof(INotifyIcon))
            {
                if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
                    return typeof(Win.NotifyIcon);
                else if (RuntimeInformation.IsOSPlatform(OSPlatform.Linux))
                    return null;
                else if (RuntimeInformation.IsOSPlatform(OSPlatform.OSX))
                    return null;
                else return null;
            }
            
            throw new NotImplementedException("This type does not have a platform-specific implementation!");
        }
    }
}
