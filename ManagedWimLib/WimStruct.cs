﻿/*
    Licensed under LGPLv3

    Derived from wimlib's original header files
    Copyright (C) 2012, 2013, 2014 Eric Biggers

    C# Wrapper written by Hajin Jang
    Copyright (C) 2017-2018 Hajin Jang

    This file is free software; you can redistribute it and/or modify it under
    the terms of the GNU Lesser General Public License as published by the Free
    Software Foundation; either version 3 of the License, or (at your option) any
    later version.

    This file is distributed in the hope that it will be useful, but WITHOUT
    ANY WARRANTY; without even the implied warranty of MERCHANTABILITY or FITNESS
    FOR A PARTICULAR PURPOSE. See the GNU Lesser General Public License for more
    details.

    You should have received a copy of the GNU Lesser General Public License
    along with this file; if not, see http://www.gnu.org/licenses/.
*/

using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading.Tasks;

namespace ManagedWimLib
{
    #region WimStruct
    public class Wim : IDisposable
    { // Wrapper of WimStruct and wimlib API
        #region Const
        public const int NoImage = 0;
        public const int AllImages = -1;
        public const int DefaultThreads = 0;
        public const string PathSeparator = @"\";
        public const string RootPath = @"\";
        #endregion

        #region Field
        public IntPtr Ptr { get; private set; }
        private ManagedProgressCallback managedCallback;
        #endregion

        #region Properties
        public static bool Loaded => NativeMethods.Loaded;
        public static string ErrorFile => NativeMethods.ErrorFile;
        #endregion

        #region Constructor (private)
        private Wim(IntPtr ptr)
        {
            if (!NativeMethods.Loaded)
                throw new InvalidOperationException(NativeMethods.MsgInitFirstError);

            Ptr = ptr;
        }
        #endregion

        #region Disposable Pattern
        ~Wim()
        {
            this.Dispose(false);
        }

        public void Dispose()
        {
            Dispose(true);
            GC.SuppressFinalize(this);
        }

        protected virtual void Dispose(bool disposing)
        {
            if (disposing)
            {
                if (Ptr != IntPtr.Zero)
                {
                    RegisterCallback(null);
                    NativeMethods.Free(Ptr);
                    Ptr = IntPtr.Zero;
                }
            }
        }
        #endregion

        #region Global - (Static) GlobalInit, GlobalCleanup
        public static void GlobalInit(string dllPath, InitFlags initFlags = InitFlags.DEFAULT)
        {
            if (NativeMethods.Loaded)
                throw new InvalidOperationException(NativeMethods.MsgAlreadyInited);
            
            if (dllPath == null) throw new ArgumentNullException(nameof(dllPath));
            if (!File.Exists(dllPath)) throw new FileNotFoundException("Specified dll does not exist");

            NativeMethods.hModule = NativeMethods.LoadLibrary(dllPath);
            if (NativeMethods.hModule.IsInvalid)
                throw new ArgumentException($"Unable to load [{dllPath}]", new Win32Exception());

            // Check if dll is valid (wimlib-15.dll)
            if (NativeMethods.GetProcAddress(NativeMethods.hModule, "wimlib_open_wim") == IntPtr.Zero)
            {
                GlobalCleanup();
                throw new ArgumentException($"[{dllPath}] is not a valid wimlib library");
            }

            try
            {
                NativeMethods.LoadFuntions();

                // Set ErrorFile and PrintError
                NativeMethods.ErrorFile = Path.GetTempFileName();
                WimLibException.CheckWimLibError(NativeMethods.SetErrorFile(NativeMethods.ErrorFile));
                Wim.SetPrintErrors(true);

                ErrorCode ret = NativeMethods.GlobalInit(initFlags);
                WimLibException.CheckWimLibError(ret);
            }
            catch (Exception)
            {
                GlobalCleanup();
                throw;
            }
        }

        public static void GlobalCleanup()
        {
            if (NativeMethods.Loaded)
            {
                NativeMethods.GlobalCleanup();

                NativeMethods.ResetFuntions();

                NativeMethods.hModule.Close();
                NativeMethods.hModule = null;

                if (File.Exists(ErrorFile))
                    File.Delete(ErrorFile);
            }
            else
            {
                throw new InvalidOperationException(NativeMethods.MsgInitFirstError);
            }
        }
        #endregion

        #region Error - (Static) GetErrorString, GetLastError, SetPrintErrors
        /// <summary>
        /// Convert a wimlib error code into a string describing it.
        /// </summary>
        /// <param name="code">An error code returned by one of wimlib's functions.</param>
        /// <returns>
        /// string describing the error code.
        /// If the value was unrecognized, then the resulting string will be "Unknown error".
        /// </returns>
        public static string GetErrorString(ErrorCode code)
        {
            if (!NativeMethods.Loaded)
                throw new InvalidOperationException(NativeMethods.MsgInitFirstError);

            IntPtr ptr = NativeMethods.GetErrorString(ErrorCode.INVALID_IMAGE);
            return Marshal.PtrToStringUni(ptr);
        }

        public static string GetLastError()
        {
            if (NativeMethods.ErrorFile == null)
                return null;
            if (NativeMethods.PrintErrorsEnabled == false)
                return null;

            using (FileStream fs = new FileStream(NativeMethods.ErrorFile, FileMode.Open, FileAccess.Read, FileShare.ReadWrite))
            using (StreamReader r = new StreamReader(fs, Encoding.Unicode, false))
            {
                var lines = r.ReadToEnd().Split('\n').Where(x => !string.IsNullOrWhiteSpace(x));
                return lines.LastOrDefault();
            }
        }

        /// <summary>
        /// Set whether wimlib can print error and warning messages to the error file, which defaults to standard error.
        /// Error and warning messages may provide information that cannot be determined only from returned error codes.
        /// 
        /// By default, error messages are not printed.
        /// This setting applies globally (it is not per-WIM).
        /// This can be called before Wim.GlobalInit().
        /// </summary>
        /// <param name="showMessages">
        /// true if messages are to be printed;
        /// false if messages are not to be printed.
        /// </param>
        /// <exception cref="WimLibException">wimlib did not return ErrorCode.SUCCESS.</exception>
        public static void SetPrintErrors(bool showMessages)
        {
            if (!NativeMethods.Loaded)
                throw new InvalidOperationException(NativeMethods.MsgInitFirstError);

            ErrorCode ret = NativeMethods.SetPrintErrors(showMessages);
            WimLibException.CheckWimLibError(ret);

            NativeMethods.PrintErrorsEnabled = showMessages;
        }
        #endregion

        #region Add - AddEmptyImage, AddImage, AddImageMultiSource, AddTree
        /// <summary>
        /// Append an empty image to a ::WimStruct.
        ///
        /// The new image will initially contain no files or directories, although if
        /// written without further modifications, then a root directory will be created
        /// automatically for it.
        /// </summary>
        /// <remarks>
        /// After calling this function, you can use Wim.UpdateImage() to add files to the new image.
        /// This gives you more control over making the new image compared to calling Wim.AddImage() or Wim.AddImageMultisource().
        /// </remarks>
        /// <param name="name">
        /// Name to give the new image. 
        /// If null or empty, the new image is given no name. 
        /// If nonempty, it must specify a name that does not already exist in wim.
        /// </param>
        /// <returns>If non-null, the index of the newly added image is returned in this location.</returns>
        /// <exception cref="WimLibException">wimlib did not return ErrorCode.SUCCESS.</exception>
        public int AddEmptyImage(string name)
        {
            ErrorCode ret = NativeMethods.AddEmptyImage(Ptr, name, out int newIdx);
            WimLibException.CheckWimLibError(ret);

            return newIdx;
        }

        /// <summary>
        /// Add an image to a WimStruct from an on-disk directory tree or NTFS volume.
        /// </summary>
        /// <param name="source">
        /// A path to a directory or unmounted NTFS volume that will be captured as a WIM image.
        /// </param>
        /// <param name="name">
        /// Name to give the new image. 
        /// If null or empty, the new image is given no name.
        /// If nonempty, it must specify a name that does not already exist in wim.
        /// </param>
        /// <param name="configFile">
        /// Path to capture configuration file, or null.
        /// This file may specify, among other things, which files to exclude from capture.  
        /// 
        /// If null, the default capture configuration will be used.
        /// Ordinarily, the default capture configuration will result in no files being excluded from capture purely based on name;
        /// however, the AddFlags.WINCONFIG and AddFlags.WIMBOOT flags modify the default.
        /// </param>
        /// <param name="addFlags">
        /// Bitwise OR of AddFlags.
        /// </param>
        /// <remarks>
        /// The directory tree or NTFS volume is scanned immediately to load the dentry tree into memory, and file metadata is read.
        /// However, actual file data may not be read until the WimStruct is persisted to disk using Wim.Write() or Wim.Overwrite().
        ///
        /// See the documentation for the wimlib-imagex program for more information
        /// about the "normal" capture mode versus the NTFS capture mode (entered by providing the flag AddFlags.NTFS).
        ///
        /// Note that no changes are committed to disk until Wim.Write() or Wim.Overwrite() is called.
        /// </remarks>
        /// <exception cref="WimLibException">wimlib did not return ErrorCode.SUCCESS.</exception>
        public void AddImage(string source, string name, string configFile, AddFlags addFlags)
        {
            ErrorCode ret = NativeMethods.AddImage(Ptr, source, name, configFile, addFlags);
            WimLibException.CheckWimLibError(ret);
        }

        /// <summary>
        /// Equivalent to Wim.AddImage() except it allows for multiple sources to be combined into a single WIM image.
        /// </summary>
        /// <param name="sources">
        /// Array of capture sources.
        /// </param>
        /// <param name="name">
        /// Name to give the new image. 
        /// If null or empty, the new image is given no name.
        /// If nonempty, it must specify a name that does not already exist in wim.
        /// </param>
        /// <param name="configFile">
        /// Path to capture configuration file, or null.
        /// This file may specify, among other things, which files to exclude from capture.  
        /// 
        /// If null, the default capture configuration will be used.
        /// Ordinarily, the default capture configuration will result in no files being excluded from capture purely based on name;
        /// however, the AddFlags.WINCONFIG and AddFlags.WIMBOOT flags modify the default.
        /// </param>
        /// <param name="addFlags">Bitwise OR of AddFlags.</param>
        /// <exception cref="WimLibException">wimlib did not return ErrorCode.SUCCESS.</exception>
        public void AddImageMultiSource(IEnumerable<CaptureSource> sources, string name, string configFile, AddFlags addFlags)
        {
            ErrorCode ret = NativeMethods.AddImageMultiSource(Ptr, sources.ToArray(), new IntPtr(sources.Count()), name, configFile, addFlags);
            WimLibException.CheckWimLibError(ret);
        }

        /// <summary>
        /// Add the file or directory tree at fsSourcePath on the filesystem to the location wimTargetPath
        /// within the specified image of thewim.
        ///
        /// This just builds an appropriate AddCommand and passes it to Wim.UpdateImage().
        /// </summary>
        /// <param name="image"></param>
        /// <param name="fsSourcePath"></param>
        /// <param name="wimTargetPath"></param>
        /// <param name="addFlags">Bitwise OR of AddFlags.</param>
        /// /// <exception cref="WimLibException">wimlib did not return ErrorCode.SUCCESS.</exception>
        public void AddTree(int image, string fsSourcePath, string wimTargetPath, AddFlags addFlags)
        {
            ErrorCode ret = NativeMethods.AddTree(Ptr, image, fsSourcePath, wimTargetPath, addFlags);
            WimLibException.CheckWimLibError(ret);
        }
        #endregion

        #region Create - (Static) CreateNewWim
        /// <summary>
        /// Create a WimStruct which initially contains no images and is not backed by
        /// an on-disk file.
        /// </summary>
        /// <param name="compType">
        /// The "output compression type" to assign to the WimStruct.
        /// This is the compression type that will be used if the WimStruct is later persisted to an on-disk file using Wim.Write().
        /// </param>
        /// <exception cref="WimLibException">wimlib did not return ErrorCode.SUCCESS.</exception>
        public static Wim CreateNewWim(CompressionType compType)
        {
            if (!NativeMethods.Loaded)
                throw new InvalidOperationException(NativeMethods.MsgInitFirstError);

            ErrorCode ret = NativeMethods.CreateNewWim(compType, out IntPtr wimPtr);
            WimLibException.CheckWimLibError(ret);

            return new Wim(wimPtr);
        }
        #endregion

        #region Delete - DeleteImage, DeletePath
        /// <summary>
        /// Delete an image, or all images, from a WimStruct.
        /// </summary>
        /// <remarks>
        /// Note that no changes are committed to disk until Wim.Write() or Wim.Overwrite() is called.
        /// </remarks>
        /// <param name="image">The 1-based index of the image to delete, or WimLibConst.ALL_IMAGES to delete all images.</param>
        /// <exception cref="WimLibException">wimlib did not return ErrorCode.SUCCESS.</exception>
        public void DeleteImage(int image)
        {
            ErrorCode ret = NativeMethods.DeleteImage(Ptr, image);
            WimLibException.CheckWimLibError(ret);
        }

        /// <summary>
        /// Delete the path from the specified image of the wim.
        /// </summary>
        /// <remarks>
        /// This just builds an appropriate DeleteCommand and passes it to Wim.UpdateImage().
        /// </remarks>
        /// <param name="image">The 1-based index of the image to delete, or Wim.AllImages to delete all images.</param>
        /// <param name="path">Path to be deleted from the specified image of the wim</param>
        /// <param name="deleteFlags">Bitwise OR of DeleteFlags.</param>
        /// <exception cref="WimLibException">wimlib did not return ErrorCode.SUCCESS.</exception>
        public void DeletePath(int image, string path, DeleteFlags deleteFlags)
        {
            ErrorCode ret = NativeMethods.DeletePath(Ptr, image, path, deleteFlags);
            WimLibException.CheckWimLibError(ret);
        }
        #endregion

        #region Export - ExportImage
        /// <summary>
        /// Export an image, or all images, from a WimStruct into another WimStruct.
        ///
        /// Specifically, if the destination WimStruct contains n images, then
        /// the source image(s) will be appended, in order, starting at destination index n + 1.
        /// By default, all image metadata will be exported verbatim, but certain changes can be made by passing appropriate parameters.
        /// </summary>
        /// <param name="srcImage">The 1-based index of the image from src_wim to export, or Wim.AllImages</param>
        /// <param name="destWim">The WimStruct to which to export the images.</param>
        /// <param name="destName">
        /// For single-image exports, the name to give the exported image in destWim.
        /// If left null, the name from srcWim is used.
        /// For WimLibConst.AllImages exports, this parameter must be left null; in that case, the names are all taken from src_wim.
        /// This parameter is overridden by ExportFlags.NO_NAMES.</param>
        /// <param name="destDescription">
        /// For single-image exports, the description to give the exported image in the new WIM file.
        /// If left null, the description from src_wim is used.
        /// For WimLib.ALL_IMAGES exports, this parameter must be left null; in that case, the description are all taken from src_wim.
        /// This parameter is overridden by ExportFlags.NO_DESCRIPTIONS.
        /// </param>
        /// <param name="exportFlags">Bitwise OR of flags with ExportFlag.</param>
        /// <remarks>
        /// Wim.ExportImage() is only an in-memory operation; no changes are committed to disk until Wim.Write() or Wim.Overwrite() is called.
        /// 
        /// A limitation of the current implementation of Wim.ExportImage() is that
        /// the directory tree of a source or destination image cannot be updated
        /// following an export until one of the two images has been freed from memory.
        /// </remarks>
        /// <exception cref="WimLibException">wimlib did not return ErrorCode.SUCCESS.</exception>
        public void ExportImage(int srcImage, Wim destWim, string destName, string destDescription, ExportFlags exportFlags)
        {
            ErrorCode ret = NativeMethods.ExportImage(Ptr, srcImage, destWim.Ptr, destName, destDescription, exportFlags);
            WimLibException.CheckWimLibError(ret);
        }
        #endregion

        #region Extract - ExtractImage, ExtractPath, ExtractPaths, ExtractPathList
        /// <summary>
        /// Extract an image, or all images, from a WimStruct.
        /// </summary>
        /// <param name="wim">
        /// The WIM from which to extract the image(s), specified as a pointer to the
        /// WimStruct for a standalone WIM file, a delta WIM file, or part 1 of a split WIM.
        /// In the case of a WIM file that is not standalone, this WimStruct must have had any needed external resources previously
        /// referenced using Wim.ReferenceResources() or Wim.ReferenceResourceFiles().
        /// </param>
        /// <param name="image">
        /// The 1-based index of the image to extract, or Wim.AllImages to extract all images.
        /// Note: Wim.AllImages is unsupported in NTFS-3G extraction mode.
        /// </param>
        /// <param name="target">
        /// A null-terminated string which names the location to which the image(s) will be extracted.  
        /// By default, this is interpreted as a path to a directory.
        /// Alternatively, if ExtractFlags.NTFS is specified in extractFlags, then this is interpreted as a path to an unmounted NTFS volume.
        /// </param>
        /// <param name="extract_flags">
        /// Bitwise OR of ExtractFlags.
        /// </param>
        /// <remarks>
        /// The exact behavior of how wimlib extracts files from a WIM image is controllable by the extractFlags parameter, 
        /// but there also are differences depending on the platform (UNIX-like vs Windows).
        /// See the documentation for wimapply for more information, including about the NTFS-3G extraction mode.
        /// </remarks>
        /// <exception cref="WimLibException">wimlib did not return ErrorCode.SUCCESS.</exception>
        public void ExtractImage(int image, string target, ExtractFlags extractFlags)
        {
            ErrorCode ret = NativeMethods.ExtractImage(Ptr, image, target, extractFlags);
            WimLibException.CheckWimLibError(ret);
        }

        /// <summary>
        /// Extract one path (files or directory trees) from the specified WIM image.
        /// </summary>
        /// <remarks>
        /// By default, each path will be extracted to a corresponding subdirectory of the target based on its location in the image.
        /// For example, if one of the paths to extract is /Windows/explorer.exe and the target is outdir, 
        /// the file will be extracted to outdir/Windows/explorer.exe.
        /// This behavior can be changed by providing the flag ExtractFlags.NO_PRESERVE_DIR_STRUCTURE, 
        /// which will cause each file or directory tree to be placed directly in the target directory 
        /// --- so the same example would extract /Windows/explorer.exe to outdir/explorer.exe.
        ///
        /// With globbing turned off (the default), paths are always checked for existence strictly; 
        /// that is, if any path to extract does not exist in the  image, then nothing is extracted 
        /// and the function fails with ErrorCode.PATH_DOES_NOT_EXIST.
        /// But with globbing turned on (ExtractFlags.GLOB_PATHS specified), globs are by default permitted to match no files,
        /// and there is a flag (ExtractFlags.STRICT_GLOB) to enable the strict behavior if desired.
        ///
        /// Symbolic links are not dereferenced when paths in the image are interpreted.
        /// </remarks>
        /// <param name="image">
        /// The 1-based index of the WIM image from which to extract the paths.
        /// </param>
        /// <param name="target">
        /// Directory to which to extract the paths.
        /// </param>
        /// <param name="path">
        /// Path to extract, must be the absolute path to a file or directory within the image.
        /// Path separators may be either forwards or backwards slashes, and leading path separators are optional.
        /// The paths will be interpreted either case-sensitively (UNIX default) or case-insensitively (Windows default);
        /// however, the case sensitivity can be configured explicitly at library initialization time by passing an
        /// appropriate flag to AssemblyInit().
        /// </param>
        /// <param name="extract_flags">
        /// Bitwise OR of flags prefixed with WIMLIB_EXTRACT_FLAG.
        /// </param>
        /// <exception cref="WimLibException">wimlib did not return ErrorCode.SUCCESS.</exception>
        public void ExtractPath(int image, string target, string path, ExtractFlags extractFlags)
        {
            ErrorCode ret = NativeMethods.ExtractPaths(Ptr, image, target, new string[1] { path }, new IntPtr(1), extractFlags);
            WimLibException.CheckWimLibError(ret);
        }

        /// <summary>
        /// Extract zero or more paths (files or directory trees) from the specified WIM image.
        /// </summary>
        /// <remarks>
        /// By default, each path will be extracted to a corresponding subdirectory of the target based on its location in the image.
        /// For example, if one of the paths to extract is /Windows/explorer.exe and the target is outdir, 
        /// the file will be extracted to outdir/Windows/explorer.exe.
        /// This behavior can be changed by providing the flag ExtractFlags.NO_PRESERVE_DIR_STRUCTURE, 
        /// which will cause each file or directory tree to be placed directly in the target directory 
        /// --- so the same example would extract /Windows/explorer.exe to outdir/explorer.exe.
        ///
        /// With globbing turned off (the default), paths are always checked for existence strictly; 
        /// that is, if any path to extract does not exist in the  image, then nothing is extracted 
        /// and the function fails with ErrorCode.PATH_DOES_NOT_EXIST.
        /// But with globbing turned on (ExtractFlags.GLOB_PATHS specified), globs are by default permitted to match no files,
        /// and there is a flag (ExtractFlags.STRICT_GLOB) to enable the strict behavior if desired.
        ///
        /// Symbolic links are not dereferenced when paths in the image are interpreted.
        /// </remarks>
        /// <param name="image">
        /// The 1-based index of the WIM image from which to extract the paths.
        /// </param>
        /// <param name="target">
        /// Directory to which to extract the paths.
        /// </param>
        /// <param name="paths">
        /// Array of paths to extract.
        /// Each element must be the absolute path to a file or directory within the image.
        /// Path separators may be either forwards or backwards slashes, and leading path separators are optional.
        /// The paths will be interpreted either case-sensitively (UNIX default) or case-insensitively (Windows default);
        /// however, the case sensitivity can be configured explicitly at library initialization time by passing an
        /// appropriate flag to AssemblyInit().
        /// </param>
        /// <param name="num_paths">
        /// Number of paths specified in paths.
        /// </param>
        /// <param name="extract_flags">
        /// Bitwise OR of flags prefixed with WIMLIB_EXTRACT_FLAG.
        /// </param>
        /// <exception cref="WimLibException">wimlib did not return ErrorCode.SUCCESS.</exception>
        public void ExtractPaths(int image, string target, IEnumerable<string> paths, ExtractFlags extractFlags)
        {
            ErrorCode ret = NativeMethods.ExtractPaths(Ptr, image, target, paths.ToArray(), new IntPtr(paths.Count()), extractFlags);
            WimLibException.CheckWimLibError(ret);
        }

        /// <summary>
        /// Similar to ExtractPaths(), but the paths to extract from the WimStruct.
        /// image are specified in the ASCII, UTF-8, or UTF-16LE text file named by
        /// pathListFile which itself contains the list of paths to use, one per line.
        /// </summary>
        /// <remarks>
        /// Leading and trailing whitespace is ignored.
        /// Empty lines and lines beginning with the ';' or '#' characters are ignored.
        /// No quotes are needed, as paths are otherwise delimited by the newline character.
        /// However, quotes will be stripped if present.
        /// </remarks>
        /// <exception cref="WimLibException">wimlib did not return ErrorCode.SUCCESS.</exception>
        public void ExtractPathList(int image, string target, string pathListFile, ExtractFlags extractFlags)
        {
            ErrorCode ret = NativeMethods.ExtractPathList(Ptr, image, target, pathListFile, extractFlags);
            WimLibException.CheckWimLibError(ret);
        }
        #endregion

        #region GetImageInfo - GetImageDescription, GetImageName, GetImageProperty
        /// <summary>
        /// Get the description of the specified image.
        /// Equivalent to GetImageProperty(image, "DESCRIPTION")
        /// </summary>
        /// <param name="image">The 1-based index of the image for which to set the property.</param>
        public string GetImageDescription(int image)
        {
            IntPtr ptr = NativeMethods.GetImageDescription(Ptr, image);
            if (ptr == IntPtr.Zero)
                return null;
            else
                return Marshal.PtrToStringUni(ptr);
        }

        /// <summary>
        /// Get the name of the specified image.
        /// Equivalent to GetImageProperty(image, "NAME")
        /// </summary>
        /// <param name="image">The 1-based index of the image for which to set the property.</param>
        /// <remarks>
        /// GetImageName() will return an empty string if the image is unnamed 
        /// whereas GetImageProperty() may return null in that case.
        /// </remarks>
        public string GetImageName(int image)
        {
            IntPtr ptr = NativeMethods.GetImageName(Ptr, image);
            if (ptr == IntPtr.Zero)
                return null;
            else
                return Marshal.PtrToStringUni(ptr);
        }

        /// <summary>
        /// Since wimlib v1.8.3: get a per-image property from the WIM's XML document.
        /// </summary>
        /// <remarks>
        /// This is an alternative to wimlib_get_image_name() and Wim.GetImageDescription() which allows getting any simple string property.
        /// </remarks>
        /// <param name="image">The 1-based index of the image for which to set the property.</param>
        /// <param name="property_name">
        /// The name of the image property, for example "NAME", "DESCRIPTION", or "TOTALBYTES".
        /// The name can contain forward slashes to indicate a nested XML element; for example, "WINDOWS/VERSION/BUILD"
        /// indicates the BUILD element nested within the VERSION element nested within the WINDOWS element.
        /// Since wimlib v1.9.0, a bracketed number can be used to indicate one of several identically-named elements; for example, 
        /// "WINDOWS/LANGUAGES/LANGUAGE[2]" indicates the second "LANGUAGE" element nested within the "WINDOWS/LANGUAGES" element.
        /// Note that element names are case sensitive.
        /// </param>
        /// <returns>
        /// The property's value as a  string, or NULL if there is no such property. 
        /// </returns>
        public string GetImageProperty(int image, string propertyName)
        {
            IntPtr ptr = NativeMethods.GetImageProperty(Ptr, image, propertyName);
            if (ptr == IntPtr.Zero)
                return null;
            else
                return Marshal.PtrToStringUni(ptr);
        }
        #endregion

        #region GetWimInfo - GetWimInfo, GetXmlData, IsImageNameInUse, ResolveImage
        /// <summary>
        /// Get basic information about a WIM file.
        /// </summary>
        /// <returns>Return 0</returns>
        public WimInfo GetWimInfo()
        {
            // This function always return 0, so no need to check exception
            IntPtr infoPtr = Marshal.AllocHGlobal(Marshal.SizeOf(typeof(WimInfo)));
            try
            {
                NativeMethods.GetWimInfo(Ptr, infoPtr);
                return (WimInfo)Marshal.PtrToStructure(infoPtr, typeof(WimInfo));
            }
            finally
            {
                Marshal.FreeHGlobal(infoPtr);
            }
        }

        /// <summary>
        /// Read a WIM file's XML document into an in-memory buffer.
        ///
        /// The XML document contains metadata about the WIM file and the images stored in it.
        /// </summary>
        /// <returns>string contains XML document is returned.</returns>
        public string GetXmlData()
        {
            IntPtr buffer = IntPtr.Zero;
            IntPtr bufferSize = IntPtr.Zero;

            NativeMethods.GetXmlData(Ptr, ref buffer, ref bufferSize);

            // bufferSize is a length in byte.
            // Marshal.PtrStringUni expects length of characters.
            // Since xml is returned in UTF-16LE, divide by two.
            int charLen = bufferSize.ToInt32() / 2;
            return Marshal.PtrToStringUni(buffer, charLen).Trim();
        }

        /// <summary>
        /// Determine if an image name is already used by some image in the WIM.
        /// </summary>
        /// <param name="name">The name to check.</param>
        /// <returns>
        /// true if there is already an image in wim named name;
        /// false if there is no image named name in wim.
        /// If name is NULL or the empty string, then false is returned.
        /// </returns>
        public bool IsImageNameInUse(string name)
        {
            return NativeMethods.IsImageNameInUse(Ptr, name);
        }

        /// <summary>
        /// Translate a string specifying the name or number of an image in the WIM into the number of the image.
        /// The images are numbered starting at 1.
        /// </summary>
        /// <param name="imageNameOrNum">
        /// A string specifying the name or number of an image in the WIM.
        /// If it parses to a positive integer, this integer is taken to specify the number of the image, indexed starting at 1.
        /// Otherwise, it is taken to be the name of an image, as given in the XML data for the WIM file.
        /// It also may be the keyword "all" or the string "*", both of which will resolve to Wim.AllImages.
        /// </param>
        /// <returns>
        /// If the string resolved to a single existing image, the number of that image, indexed starting at 1, is returned.
        /// If the keyword "all" or "*" was specified, Wim.AllImages is returned.
        /// Otherwise, Wim.NoImage is returned.
        /// 
        /// If imageNameOr_Num was null or the empty string, Wim.NO_IMAGE is returned, even if one or more images in wim has no name.
        /// (Since a WIM may have multiple unnamed images, an unnamed image must be specified by index to eliminate the ambiguity.)
        /// </returns>
        public int ResolveImage(string imageNameOrNum)
        {
            return NativeMethods.ResolveImage(Ptr, imageNameOrNum);
        }
        #endregion

        #region Iterate - IterateDirTree, IterateLookupTable
        /// <summary>
        /// Iterate through a file or directory tree in a WIM image.
        /// By specifying appropriate flags and a callback function, you can get the attributes of a
        /// file in the image, get a directory listing, or even get a listing of the entire image.
        /// </summary>
        /// <param name="image">
        /// The 1-based index of the image that contains the files or directories to iterate over,
        /// or Wim.AllImages to iterate over all images.
        /// </param>
        /// <param name="path">
        /// Path in the image at which to do the iteration.
        /// </param>
        /// <param name="iterateFlags">
        /// Bitwise OR of IterateFlags.
        /// </param>
        /// <param name="callback">
        /// A callback function that will receive each directory entry.
        /// </param>
        /// <param name="userData">
        /// An extra parameter that will always be passed to the callback function.
        /// </param>
        /// <exception cref="WimLibException">wimlib did not return ErrorCode.SUCCESS.</exception>
        public void IterateDirTree(int image, string path, IterateFlags iterateFlags, IterateDirTreeCallback callback, object userData = null)
        {
            ManagedIterateDirTreeCallback cb = new ManagedIterateDirTreeCallback(callback, userData);

            ErrorCode ret = NativeMethods.IterateDirTree(Ptr, image, path, iterateFlags, cb.NativeFunc, IntPtr.Zero);
            WimLibException.CheckWimLibError(ret);
        }

        /// <summary>
        /// Iterate through the blob lookup table of a WimStruct.
        /// This can be used to directly get a listing of the unique "blobs" contained in a WIM file, 
        /// which are deduplicated over all images.
        /// </summary>
        /// <param name="callback">A callback function that will receive each blob.</param>
        /// <param name="userData">An extra parameter that will always be passed to the callback function</param>
        /// <exception cref="WimLibException">wimlib did not return ErrorCode.SUCCESS.</exception>
        public void IterateLookupTable(IterateLookupTableCallback callback, object userData = null)
        {
            ManagedIterateLookupTableCallback cb = new ManagedIterateLookupTableCallback(callback, userData);

            ErrorCode ret = NativeMethods.IterateLookupTable(Ptr, 0, cb.NativeFunc, IntPtr.Zero);
            WimLibException.CheckWimLibError(ret);
        }
        #endregion

        #region Join - (Static) Join
        /// <summary>
        /// Join a split WIM into a stand-alone (one-part) WIM.
        /// </summary>
        /// <remarks>
        /// Note: wimlib is generalized enough that this function is not actually needed to join a split WIM;
        /// instead, you could open the first part of the split WIM, then reference the other parts with Wim.ReferenceResourceFiles(),
        /// thn write the joined WIM using Wim.Write().
        /// However, Wim.Join() provide an easy-to-use wrapper around this that has some advantages (e.g.  extra sanity checks).
        /// </remarks>
        /// <param name="swms">
        /// An array of strings that gives the filenames of all parts of the split WIM.
        /// No specific order is required, but all parts must be included with no duplicates.
        /// </param>
        /// <param name="outputPath">The path to write the joined WIM file to.</param>
        /// <param name="swmOpenFlags">Open flags for the split WIM parts (e.g. OpenFlags.CHECK_INTEGRITY).</param>
        /// <param name="wimWriteFlags">Bitwise OR of relevant WriteFlags, which will be used to write the joined WIM.</param>
        /// <exception cref="WimLibException">wimlib did not return ErrorCode.SUCCESS.</exception>
        public static void Join(IEnumerable<string> swms, string outputPath, OpenFlags swmOpenFlags, WriteFlags wimWriteFlags)
        {
            ErrorCode ret = NativeMethods.Join(swms.ToArray(), (uint)swms.Count(), outputPath, swmOpenFlags, wimWriteFlags);
            WimLibException.CheckWimLibError(ret);
        }

        /// <summary>
        /// Same as Wim.Join(), but allows specifying a progress function.
        /// </summary>
        /// <remarks>
        /// The progress function will receive the write progress messages, such as ProgressMsg.WRITE_STREAMS, while writing the joined WIM.
        /// In addition, if OpenFlags.CHECK_INTEGRITY is specified in swmOpenFlags, the progress function will receive a series of
        /// ProgressMsg.VERIFY_INTEGRITY messages when each of the split WIM parts is opened.
        /// </remarks>
        /// <param name="swms">
        /// An array of strings that gives the filenames of all parts of the split WIM.
        /// No specific order is required, but all parts must be included with no duplicates.
        /// </param>
        /// <param name="outputPath">The path to write the joined WIM file to.</param>
        /// <param name="swmOpenFlags">Open flags for the split WIM parts (e.g. OpenFlags.CHECK_INTEGRITY).</param>
        /// <param name="wimWriteFlags">Bitwise OR of relevant WriteFlags, which will be used to write the joined WIM.</param>
        /// <exception cref="WimLibException">wimlib did not return ErrorCode.SUCCESS.</exception>
        public static void Join(IEnumerable<string> swms, string outputPath, OpenFlags swmOpenFlags, WriteFlags wimWriteFlags,
            ProgressCallback callback, object userData = null)
        {
            ManagedProgressCallback mCallback = new ManagedProgressCallback(callback, userData); 

            ErrorCode ret = NativeMethods.JoinWithProgress(swms.ToArray(), (uint)swms.Count(), outputPath, swmOpenFlags, wimWriteFlags,
                mCallback.NativeFunc, IntPtr.Zero);
            WimLibException.CheckWimLibError(ret);
        }
        #endregion

        #region Open - (Static) OpenWim
        /// <summary>
        /// Open a WIM file and create a instance of Wim class for it.
        /// </summary>
        /// <param name="wimFile">The path to the WIM file to open.</param>
        /// <param name="openFlags">Bitwise OR of flags prefixed with WIMLIB_OPEN_FLAG.</param>
        /// <returns>
        /// On success, a new instance of Wim class backed by the specified
        ///	on-disk WIM file is returned. This instance must be disposed 
        ///	when finished with it.
        ///	</returns>
        ///	<exception cref="WimLibException">wimlib did not return ErrorCode.SUCCESS.</exception>
        public static Wim OpenWim(string wimFile, OpenFlags openFlags)
        {
            if (!NativeMethods.Loaded)
                throw new InvalidOperationException(NativeMethods.MsgInitFirstError);

            ErrorCode ret = NativeMethods.OpenWim(wimFile, openFlags, out IntPtr wimPtr);
            if (ret != ErrorCode.SUCCESS)
                throw new WimLibException(ret);

            return new Wim(wimPtr);
        }

        /// <summary>
        /// Same as OpenWim(), but allows specifying a progress function and progress context.  
        /// </summary>
        /// <remarks>
        /// If successful, the progress function will be registered in the newly open WimStruct,
        /// as if by an automatic call to Wim.RegisterCallback().
        /// 
        /// In addition, if OpenFlags.CHECK_INTEGRITY is specified in openFlags,
        /// then the callback function will receive ProgressMsg.VERIFY_INTEGRITY messages while checking the WIM file's integrity.
        /// </remarks>
        /// <param name="wimFile">The path to the WIM file to open.</param>
        /// <param name="openFlags">Bitwise OR of flags prefixed with WIMLIB_OPEN_FLAG.</param>
        /// <returns>
        /// On success, a new instance of Wim class backed by the specified on-disk WIM file is returned.
        ///	This instance must be disposed when finished with it.
        ///	</returns>
        ///	<exception cref="WimLibException">wimlib did not return ErrorCode.SUCCESS.</exception>
        public static Wim OpenWim(string wimFile, OpenFlags openFlags, ProgressCallback callback, object userData = null)
        {
            if (!NativeMethods.Loaded)
                throw new InvalidOperationException(NativeMethods.MsgInitFirstError);

            if (callback == null)
                throw new ArgumentNullException(nameof(callback));

            ManagedProgressCallback mCallback = new ManagedProgressCallback(callback, userData);

            ErrorCode ret = NativeMethods.OpenWimWithProgress(wimFile, openFlags, out IntPtr wimPtr, mCallback.NativeFunc, IntPtr.Zero);
            WimLibException.CheckWimLibError(ret);

            return new Wim(wimPtr)
            {
                managedCallback = mCallback
            };
        }
        #endregion

        #region Reference - ReferenceResourceFiles, ReferenceResources, ReferenceTemplateImage
        /// <summary>
        /// Reference file data from other WIM files or split WIM parts. 
        /// This function can be used on WIMs that are not standalone, such as split or "delta" WIMs,
        /// to load additional file data before calling a function such as Wim.ExtractImage() that requires the file data to be present.
        /// </summary>
        /// <remarks>
        /// In the case of split WIMs, instance of WimStruct should be the/first part, since only the first part contains the metadata resources.
        /// In the case of delta WIMs, this should be the delta WIM rather than the WIM on which it is based.
        /// </remarks>
        /// <param name="resourceWimFile">
        /// A path to WIM file and/or split WIM parts to reference.
        /// Alternatively, when WimLibRefFlag.GLOB_ENABLE is specified in refFlags, these are treated as globs rather than literal paths.
        /// That is, using this function you can specify zero or more globs, each of which expands to one or more literal paths.
        /// </param>
        /// <param name="refFlags">
        /// Bitwise OR of RefFlags.GLOB_ENABLE and/or RefFlags.GLOB_ERR_ON_NOMATCH.
        /// </param>
        /// <param name="openFlags">
        /// Additional open flags, such as OpenFalgs.CHECK_INTEGRITY, to pass to internal calls to Wim.OpenWim() on the reference files.
        /// </param>
        /// <exception cref="WimLibException">wimlib did not return ErrorCode.SUCCESS.</exception>
        public void ReferenceResourceFile(string resourceWimFile, RefFlags refFlags, OpenFlags openFlags)
        {
            if (resourceWimFile == null) throw new ArgumentNullException(nameof(resourceWimFile));

            // [Dirty Hack to resolve SEHException]
            // If wimlib_reference_resource_files() is called with RefFlags.GLOB_ENABLE | RefFlags.GLOB_ERR_ON_NOMATCH,
            // SEHException is raised ONLY IN DEBUG MODE.
            // So as a dirty hack, emulate GLOBing by converting wildcard to list of actual files before calling wimlib.
            List<string> resources = new List<string>();
            string dirPath = Path.GetDirectoryName(resourceWimFile);
            string wildcard = Path.GetFileName(resourceWimFile);
            if (dirPath == null) dirPath = @"\";
            if (dirPath.Length == 0) dirPath = ".";
            if ((refFlags & RefFlags.GLOB_ENABLE) != 0 && wildcard.IndexOfAny(new[] { '*', '?' }) != -1)
            { // Contains Wildcard
                string removeAsterisk = StringHelper.ReplaceEx(resourceWimFile, "*", string.Empty, StringComparison.Ordinal);
                var files = Directory.EnumerateFiles(dirPath, wildcard, SearchOption.AllDirectories);
                resources.AddRange(files.Where(x => !x.Equals(removeAsterisk, StringComparison.OrdinalIgnoreCase)));
            }
            else
            {
                resources.Add(resourceWimFile);
            }

            if (resources.Count == 0 &&
                (refFlags & RefFlags.GLOB_ENABLE) != 0 && (refFlags & RefFlags.GLOB_ERR_ON_NOMATCH) != 0)
                throw new WimLibException(ErrorCode.GLOB_HAD_NO_MATCHES);

            ErrorCode ret = NativeMethods.ReferenceResourceFiles(Ptr, resources.ToArray(), (uint)resources.Count, RefFlags.DEFAULT, openFlags);
            WimLibException.CheckWimLibError(ret);
        }

        /// <summary>
        /// Reference file data from other WIM files or split WIM parts. 
        /// This function can be used on WIMs that are not standalone, such as split or "delta" WIMs,
        /// to load additional file data before calling a function such as Wim.ExtractImage() that requires the file data to be present.
        /// </summary>
        /// <remarks>
        /// In the case of split WIMs, instance of WimStruct should be the/first part, since only the first part contains the metadata resources.
        /// In the case of delta WIMs, this should be the delta WIM rather than the WIM on which it is based.
        /// </remarks>
        /// <param name="resourceWimFiles">
        /// Array of paths to WIM files and/or split WIM parts to reference.
        /// Alternatively, when WimLibRefFlag.GLOB_ENABLE is specified in refFlags, these are treated as globs rather than literal paths.
        /// That is, using this function you can specify zero or more globs, each of which expands to one or more literal paths.
        /// </param>
        /// <param name="refFlags">
        /// Bitwise OR of RefFlags.GLOB_ENABLE and/or RefFlags.GLOB_ERR_ON_NOMATCH.
        /// </param>
        /// <param name="openFlags">
        /// Additional open flags, such as OpenFlags.CHECK_INTEGRITY, to pass to internal calls to Wim.OpenWim() on the reference files.
        /// </param>
        /// <exception cref="WimLibException">wimlib did not return ErrorCode.SUCCESS.</exception>
        public void ReferenceResourceFiles(IEnumerable<string> resourceWimFiles, RefFlags refFlags, OpenFlags openFlags)
        {
            // [Dirty Hack to resolve SEHException]
            // If wimlib_reference_resource_files() is called with RefFlags.GLOB_ENABLE | RefFlags.GLOB_ERR_ON_NOMATCH,
            // SEHException is raised ONLY IN DEBUG MODE.
            // So as a dirty hack, emulate GLOBing by converting wildcard to list of actual files before calling wimlib.
            List<string> resources = new List<string>();
            foreach (string f in resourceWimFiles)
            {
                string dirPath = Path.GetDirectoryName(f);
                string wildcard = Path.GetFileName(f);
                if (dirPath == null) dirPath = @"\";
                if (dirPath.Length == 0) dirPath = ".";
                if ((refFlags & RefFlags.GLOB_ENABLE) != 0 && wildcard.IndexOfAny(new[] { '*', '?' }) != -1)
                { // Contains Wildcard
                    string removeAsterisk = StringHelper.ReplaceEx(f, "*", string.Empty, StringComparison.Ordinal);
                    var files = Directory.EnumerateFiles(dirPath, wildcard, SearchOption.AllDirectories);
                    resources.AddRange(files.Where(x => !x.Equals(removeAsterisk, StringComparison.OrdinalIgnoreCase)));
                }
                else
                {
                    resources.Add(f);
                }
            }

            if (resources.Count == 0 &&
                (refFlags & RefFlags.GLOB_ENABLE) != 0 && (refFlags & RefFlags.GLOB_ERR_ON_NOMATCH) != 0)
                throw new WimLibException(ErrorCode.GLOB_HAD_NO_MATCHES);

            ErrorCode ret = NativeMethods.ReferenceResourceFiles(Ptr, resources.ToArray(), (uint)resources.Count, RefFlags.DEFAULT, openFlags);
            WimLibException.CheckWimLibError(ret);
        }

        /// <summary>
        /// Similar to Wim.ReferenceResourceFiles(), but operates at a lower level
        /// where the caller must open the WimStruct for each referenced file itself.
        /// </summary>
        /// <param name="resourceWims">Array of pointers to the WimStruct's for additional resource WIMs or split WIM parts to reference.</param>
        public void ReferenceResources(IEnumerable<Wim> resourceWims)
        {
            IntPtr[] wims = resourceWims.Select(x => x.Ptr).ToArray();
            ErrorCode ret = NativeMethods.ReferenceResources(Ptr, wims, (uint)wims.Length, 0);
            WimLibException.CheckWimLibError(ret);
        }

        /// <summary>
        /// Declare that a newly added image is mostly the same as a prior image, but
        /// captured at a later point in time, possibly with some modifications in the intervening time. 
        /// This is designed to be used in incremental backups of the same filesystem or directory tree.
        /// </summary>
        /// <param name="newImage">The 1-based index in @p wim of the newly added image.</param>
        /// <param name="templateImage">The 1-based index in @p template_wim of the template image.</param>
        /// <remarks>
        /// This function compares the metadata of the directory tree of the newly added image against that of the old image.
        /// Any files that are present in both the newly added image and the old image and have timestamps that indicate they
        /// haven't been modified are deemed not to have been modified and have their checksums copied from the old image. 
        /// Because of this and because WIM uses single-instance streams, such files need not be read from the filesystem when
        /// the WIM is being written or overwritten.
        /// Note that these unchanged files will still be "archived" and will be logically present in the new image; 
        /// the optimization is that they don't need to actually be read from the filesystem because the WIM already contains them.
        ///
        /// This function is provided to optimize incremental backups.
        /// The resulting WIM file will still be the same regardless of whether this function is called.
        /// (This is, however, assuming that timestamps have not been manipulated or
        /// unmaintained as to trick this function into thinking a file has not been modified when really it has.
        /// To partly guard against such cases, other metadata such as file sizes will be checked as well.)
        ///
        /// This function must be called after adding the new image (e.g. with Wim.AddImage()),
        /// but before writing the updated WIM file (e.g. with Wim.Overwrite()).
        /// </remarks>
        /// <exception cref="WimLibException">wimlib did not return ErrorCode.SUCCESS.</exception>
        public void ReferenceTemplateImage(int newImage, int templateImage)
        {
            ErrorCode ret = NativeMethods.ReferenceTemplateImage(Ptr, newImage, Ptr, templateImage, 0);
            WimLibException.CheckWimLibError(ret);
        }

        /// <summary>
        /// Declare that a newly added image is mostly the same as a prior image, but
        /// captured at a later point in time, possibly with some modifications in the intervening time. 
        /// This is designed to be used in incremental backups of the same filesystem or directory tree.
        /// </summary>
        /// <param name="newImage">The 1-based index in @p wim of the newly added image.</param>
        /// <param name="template">
        /// A WimStruct containing the template image.
        /// This can be, but does not have to be, the same WimStruct as instance itself.
        /// </param>
        /// <param name="templateImage">The 1-based index in @p template_wim of the template image.</param>
        /// <remarks>
        /// This function compares the metadata of the directory tree of the newly added image against that of the old image.
        /// Any files that are present in both the newly added image and the old image and have timestamps that indicate they
        /// haven't been modified are deemed not to have been modified and have their checksums copied from the old image. 
        /// Because of this and because WIM uses single-instance streams, such files need not be read from the filesystem when
        /// the WIM is being written or overwritten.
        /// Note that these unchanged files will still be "archived" and will be logically present in the new image; 
        /// the optimization is that they don't need to actually be read from the filesystem because the WIM already contains them.
        ///
        /// This function is provided to optimize incremental backups.
        /// The resulting WIM file will still be the same regardless of whether this function is called.
        /// (This is, however, assuming that timestamps have not been manipulated or
        /// unmaintained as to trick this function into thinking a file has not been modified when really it has.
        /// To partly guard against such cases, other metadata such as file sizes will be checked as well.)
        ///
        /// This function must be called after adding the new image (e.g. with Wim.AddImage()),
        /// but before writing the updated WIM file (e.g. with Wim.Overwrite()).
        /// </remarks>
        /// <exception cref="WimLibException">wimlib did not return ErrorCode.SUCCESS.</exception>
        public void ReferenceTemplateImage(int newImage, Wim template, int templateImage)
        {
            ErrorCode ret = NativeMethods.ReferenceTemplateImage(Ptr, newImage, template.Ptr, templateImage, 0);
            WimLibException.CheckWimLibError(ret);
        }
        #endregion

        #region Callback - RegisterCallback
        /// <summary>
        /// Register a progress function with a WimStruct.
        /// </summary>
        /// <param name="callback">
        /// Pointer to the progress function to register.
        /// If the WIM already has a progress function registered, it will be replaced with this one.
        /// If null, the current progress function (if any) will be unregistered.
        /// </param>
        /// <param name="userData">
        /// The value which will be passed as the third argument to calls to progfunc.
        /// </param>
        public void RegisterCallback(ProgressCallback callback, object userData = null)
        {
            if (callback != null)
            { // RegisterCallback
                managedCallback = new ManagedProgressCallback(callback, userData);
                NativeMethods.RegisterProgressFunction(Ptr, managedCallback.NativeFunc, IntPtr.Zero);
            }
            else
            { // Delete callback
                managedCallback = null;
                NativeMethods.RegisterProgressFunction(Ptr, null, IntPtr.Zero);
            }
        }
        #endregion

        #region Rename - RenamePath
        /// <summary>
        /// Rename the source_path to the destPath in the specified image of the wim.
        /// </summary>
        /// <remarks>
        /// This just builds an appropriate Wim.RenameCommand and passes it to Wim.UpdateImage().
        /// </remarks>
        /// <exception cref="WimLibException">wimlib did not return ErrorCode.SUCCESS.</exception>
        public void RenamePath(int image, string sourcePath, string destPath)
        {
            ErrorCode ret = NativeMethods.RenamePath(Ptr, image, sourcePath, destPath);
            WimLibException.CheckWimLibError(ret);
        }
        #endregion

        #region SetImageInfo - SetImageDescription, SetImageFlags, SetImageName, SetImageProperty, SetWimInfo
        /// <summary>
        /// Change the description of a WIM image.
        /// Equivalent to SetImageProperty(image, "DESCRIPTION", description)
        /// </summary>
        /// <param name="image">The 1-based index of the image for which to set the property.</param>
        /// <param name="description">
        /// If not NULL and not empty, the property is set to this value.
        /// Otherwise, the property is removed from the XML document.
        /// </param>
        /// <exception cref="WimLibException">wimlib did not return ErrorCode.SUCCESS.</exception>
        public void SetImageDescription(int image, string description)
        {
            ErrorCode ret = NativeMethods.SetImageDescription(Ptr, image, description);
            WimLibException.CheckWimLibError(ret);
        }

        /// <summary>
        /// Change what is stored in the &lt;FLAGS&gt; element in the WIM XML document (usually something like "Core" or "Ultimate"). 
        /// Equivalent to SetImageProperty(image, "FLAGS", flags)
        /// </summary>
        /// <param name="image">The 1-based index of the image for which to set the property.</param>
        /// <param name="flags">
        /// If not NULL and not empty, the property is set to this value.
        /// Otherwise, the property is removed from the XML document.
        /// </param>
        /// <exception cref="WimLibException">wimlib did not return ErrorCode.SUCCESS.</exception>
        public void SetImageFlags(int image, string flags)
        {
            ErrorCode ret = NativeMethods.SetImageFlags(Ptr, image, flags);
            WimLibException.CheckWimLibError(ret);
        }

        /// <summary>
        /// Change the name of a WIM image.
        /// Equivalent to SetImageProperty(image, "NAME", name)
        /// </summary>
        /// <param name="image">The 1-based index of the image for which to set the property.</param>
        /// <param name="name">
        /// If not NULL and not empty, the property is set to this value.
        /// Otherwise, the property is removed from the XML document.
        /// </param>
        /// <exception cref="WimLibException">wimlib did not return ErrorCode.SUCCESS.</exception>
        public void SetImageName(int image, string name)
        {
            ErrorCode ret = NativeMethods.SetImageName(Ptr, image, name);
            WimLibException.CheckWimLibError(ret);
        }

        /// <summary>
        /// Since wimlib v1.8.3:
        /// add, modify, or remove a per-image property from the WIM's XML document.
        /// </summary>
        /// <remarks>
        /// This is an alternative to Wim.SetImageName(), Wim.SetImageDescripton(), and Wim.SetImageFlags()
        /// which allows manipulating any simple string property.
        /// </remarks>
        /// <param name="image">The 1-based index of the image for which to set the property.</param>
        /// <param name="propertyName">
        /// The name of the image property in the same format documented for Wim.GetImageProperty().
        /// 
        /// Note: if creating a new element using a bracketed index such as "WINDOWS/LANGUAGES/LANGUAGE[2]", the highest index 
        /// that can be specified is one greater than the number of existing elements with that same name, excluding the index.
        /// That means that if you are adding a list of new elements,
        /// they must be added sequentially from the first index (1) to the last index (n).
        /// </param>
        /// <param name="propertyValue">
        /// If not NULL and not empty, the property is set to this value.
        /// Otherwise, the property is removed from the XML document.
        /// </param>
        /// <exception cref="WimLibException">wimlib did not return ErrorCode.SUCCESS.</exception>
        public void SetImageProperty(int image, string propertyName, string propertyValue)
        {
            ErrorCode ret = NativeMethods.SetImageProperty(Ptr, image, propertyName, propertyValue);
            WimLibException.CheckWimLibError(ret);
        }

        /// <summary>
        /// Set basic information about a WIM.
        /// </summary>
        /// <param name="info">
        /// Pointer to a WimInfo structure that contains the information to set.
        /// Only the information explicitly specified in the which flags need be valid.
        /// </param>
        /// <param name="which">
        /// Flags that specify which information to set. 
        /// This is a bitwise OR of ChangeFlags.READONLY_FLAG, ChangeFlags.GUID, ChangeFlags.BOOT_INDEX, and/or ChangeFlags.RPFIX_FLAG.
        /// </param>
        /// <exception cref="WimLibException">wimlib did not return ErrorCode.SUCCESS.</exception>
        public void SetWimInfo(WimInfo info, ChangeFlags which)
        {
            ErrorCode ret = NativeMethods.SetWimInfo(Ptr, ref info, which);
            WimLibException.CheckWimLibError(ret);
        }


        #endregion

        #region SetOutput - SetOutputChunkSize, SetOutputPackChunkSize, SetOutputCompressionType, SetOutputPackCompressionType
        /// <summary>
        /// Set a WimStruct's output compression chunk size.
        /// This is the compression chunk size that will be used for writing non-solid resources
        /// in subsequent calls to Wim.Write() or Wim.Overwrite().
        /// A larger compression chunk size often results in a better compression ratio,
        /// but compression may be slower and the speed of random access to data may be reduced.
        /// In addition, some chunk sizes are not compatible with Microsoft software.
        /// </summary>
        /// <param name="chunkSize">
        /// The chunk size (in bytes) to set.
        /// The valid chunk sizes are dependent on the compression type.
        /// See the documentation for each CompressionType enum for more information.
        /// As a special case, if chunkSize is specified as 0,
        /// then the chunk size will be reset to the default for the currently selected output compression type.
        /// </param>
        /// <exception cref="WimLibException">wimlib did not return ErrorCode.SUCCESS.</exception>
        public void SetOutputChunkSize(uint chunkSize)
        {
            ErrorCode ret = NativeMethods.SetOutputChunkSize(Ptr, chunkSize);
            WimLibException.CheckWimLibError(ret);
        }

        /// <summary>
        /// Similar to Wim.SetOutputChunkSize(), but set the chunk size for writing solid resources.
        /// </summary>
        /// <param name="chunkSize">
        /// The chunk size (in bytes) to set.
        /// The valid chunk sizes are dependent on the compression type.
        /// See the documentation for each CompressionType enum for more information.
        /// As a special case, if chunkSize is specified as 0,
        /// then the chunk size will be reset to the default for the currently selected output compression type.
        /// </param>
        /// <exception cref="WimLibException">wimlib did not return ErrorCode.SUCCESS.</exception>
        public void SetOutputPackChunkSize(uint chunkSize)
        {
            ErrorCode ret = NativeMethods.SetOutputPackChunkSize(Ptr, chunkSize);
            WimLibException.CheckWimLibError(ret);
        }

        /// <summary>
        /// Set a WimStruct's output compression type.
        /// This is the compression type that will be used for writing non-solid resources
        /// in subsequent calls to Wim.Write() or Wim.Overwrite().
        /// </summary>
        /// <param name="compType">
        /// The compression type to set.
        /// If this compression type is incompatible with the current output chunk size,
        /// then the output chunk size will be reset to the default for the new compression type.
        /// </param>
        /// <exception cref="WimLibException">wimlib did not return ErrorCode.SUCCESS.</exception>
        public void SetOutputCompressionType(CompressionType compType)
        {
            ErrorCode ret = NativeMethods.SetOutputCompressionType(Ptr, compType);
            WimLibException.CheckWimLibError(ret);
        }

        /// <summary>
        /// Similar to Wim.SetOutputCompressionType(), but set the compression type for writing solid resources. 
        /// This cannot be CompressType.NONE.
        /// </summary>
        /// <param name="compType">
        /// The compression type to set.
        /// If this compression type is incompatible with the current output chunk size,
        /// then the output chunk size will be reset to the default for the new compression type.
        /// </param>
        /// <exception cref="WimLibException">wimlib did not return ErrorCode.SUCCESS.</exception>
        public void SetOutputPackCompressionType(CompressionType compType)
        {
            ErrorCode ret = NativeMethods.SetOutputPackCompressionType(Ptr, compType);
            WimLibException.CheckWimLibError(ret);
        }
        #endregion

        #region Split - Split
        /// <summary>
        /// Split a WIM into multiple parts.
        /// </summary>
        /// <param name="swmName">
        /// Name of the split WIM (SWM) file to create. 
        /// This will be the name of the first part.
        /// The other parts will, by default, have the same name with 2, 3, 4, ..., etc.  appended before the suffix.
        /// However, the exact names can be customized using the progress function.
        /// </param>
        /// <param name="partSize">
        /// The maximum size per part, in bytes.
        /// Unfortunately, it is not guaranteed that this will really be the maximum size per part,
        /// because some file resources in the WIM may be larger than this size,
        /// and the WIM file format provides no way to split up file resources among multiple WIMs.
        /// </param>
        /// <param name="writeFlags">
        /// Bitwise OR of WriteFlags.
        /// These flags will be used to write each split WIM part. 
        /// Specify WriteFlags.DEFAULT here to get the default behavior.
        /// </param>
        /// <exception cref="WimLibException">wimlib did not return ErrorCode.SUCCESS.</exception>
        public void Split(string swmName, ulong partSize, WriteFlags writeFlags)
        {
            ErrorCode ret = NativeMethods.Split(Ptr, swmName, partSize, writeFlags);
            WimLibException.CheckWimLibError(ret);
        }
        #endregion

        #region Verify - VerifyWim
        /// <summary>
        /// Perform verification checks on a WIM file.
        ///
        /// This function is intended for safety checking and/or debugging. 
        /// If used on a well-formed WIM file, it should always succeed.
        /// </summary>
        /// <remarks>
        /// Note: for an extra layer of verification, it is a good idea 
        /// to have used OpenFlags.CHECK_INTEGRITY when you opened the file.
        /// 
        /// If verifying a split WIM, specify the first part of the split WIM here,
        /// and reference the other parts using Wim.ReferenceResourceFiles() before calling this function.
        /// </remarks>
        /// <exception cref="WimLibException">wimlib did not return ErrorCode.SUCCESS.</exception>
        public void VerifyWim()
        {
            ErrorCode ret = NativeMethods.VerifyWim(Ptr, 0);
            WimLibException.CheckWimLibError(ret);
        }
        #endregion

        #region Update - UpdateImage
        /// <summary>
        /// Update a WIM image by adding, deleting, and/or renaming files or directories.
        /// </summary>
        /// <param name="image">The 1-based index of the image to update.</param>
        /// <param name="cmd">UpdateCommand that specify the update operations to perform.</param>
        /// <param name="updateFlags">Number of commands in cmd.</param>
        /// <exception cref="WimLibException">wimlib did not return ErrorCode.SUCCESS.</exception>
        public void UpdateImage(int image, UpdateCommand cmd, UpdateFlags updateFlags)
        {
            ErrorCode ret;
            switch (IntPtr.Size)
            {
                case 4:
                    UpdateCommand32[] cmds32 = new UpdateCommand32[1] { cmd.ToNativeStruct32() };
                    try
                    {
                        ret = NativeMethods.UpdateImage32(Ptr, image, cmds32, 1u, updateFlags);
                    }
                    finally
                    {
                        cmds32[0].Free();
                    }
                    break;
                case 8:
                    UpdateCommand64[] cmds64 = new UpdateCommand64[1] { cmd.ToNativeStruct64() };
                    try
                    {
                        ret = NativeMethods.UpdateImage64(Ptr, image, cmds64, 1u, updateFlags);
                    }
                    finally
                    {
                        cmds64[0].Free();
                    }
                    break;
                default:
                    throw new PlatformNotSupportedException();
            }
            WimLibException.CheckWimLibError(ret);
        }

        /// <summary>
        /// Update a WIM image by adding, deleting, and/or renaming files or directories.
        /// </summary>
        /// <param name="image">
        /// The 1-based index of the image to update.
        /// </param>
        /// <param name="cmds">
        /// An array of UpdateCommand's that specify the update operations to perform.
        /// </param>
        /// <param name="updateFlags">
        /// Number of commands in cmds.
        /// </param>
        /// <exception cref="WimLibException">wimlib did not return ErrorCode.SUCCESS.</exception>
        public void UpdateImage(int image, IEnumerable<UpdateCommand> cmds, UpdateFlags updateFlags)
        {
            ErrorCode ret;
            switch (IntPtr.Size)
            {
                case 4:
                    UpdateCommand32[] cmds32 = cmds.Select(x => x.ToNativeStruct32()).ToArray();
                    try
                    {
                        ret = NativeMethods.UpdateImage32(Ptr, image, cmds32, (uint)cmds32.Length, updateFlags);
                    }
                    finally
                    {
                        foreach (var cmd32 in cmds32)
                            cmd32.Free();
                    }
                    break;
                case 8:
                    UpdateCommand64[] cmds64 = cmds.Select(x => x.ToNativeStruct64()).ToArray();
                    try
                    {
                        ret = NativeMethods.UpdateImage64(Ptr, image, cmds64, (ulong)cmds64.Length, updateFlags);
                    }
                    finally
                    {
                        foreach (var cmd64 in cmds64)
                            cmd64.Free();
                    }
                    break;
                default:
                    throw new PlatformNotSupportedException();
            }
            WimLibException.CheckWimLibError(ret);
        }
        #endregion

        #region Write - Write, Overwrite
        /// <summary>
        /// Persist a WimStruct to a new on-disk WIM file.
        /// </summary>
        /// <param name="path">
        /// The path to the on-disk file to write.
        /// </param>
        /// <param name="image">
        /// Normally, specify Wim.AllImages here.
        /// This indicates that all images are to be included in the new on-disk WIM file.
        /// If for some reason you only want to include a single image, specify the 1-based index of that image instead.
        /// </param>
        /// <param name="write_flags">
        /// Bitwise OR of WriteFlags.
        /// </param>
        /// <param name="num_threads">
        /// The number of threads to use for compressing data, or 0 to have the library automatically choose an appropriate number.
        /// </param>
        /// <remarks>
        /// This brings in file data from any external locations, such as directory trees or NTFS volumes scanned with Wim.AddImage(),
        /// or other WIM files via Wim.ExportImage(), and incorporates it into a new on-disk WIM file.
        ///
        /// By default, the new WIM file is written as stand-alone.
        /// Using the WriteFlags.SKIP_EXTERNAL_WIMS flag, a "delta" WIM can be written instead.
        /// However, this function cannot directly write a "split" WIM; use Wim.Split() for that.
        /// </remarks>
        /// <exception cref="WimLibException">wimlib did not return ErrorCode.SUCCESS.</exception>
        public void Write(string path, int image, WriteFlags writeFlags, uint numThreads)
        {
            ErrorCode ret = NativeMethods.Write(Ptr, path, image, writeFlags, numThreads);
            WimLibException.CheckWimLibError(ret);
        }

        /// <summary>
        /// Commit a WimStruct to disk, updating its backing file.
        /// </summary>
        /// <param name="writeFlags">Bitwise OR of relevant WriteFlags.</param>
        /// <param name="numThreads">
        /// The number of threads to use for compressing data, 
        /// or Wim.DefaultThreads to have the library automatically choose an appropriate number.
        /// </param>
        /// <remarks>
        /// There are several alternative ways in which changes may be committed:
        ///
        /// 1. Full Rebuild : 
        /// Write the updated WIM to a temporary file, then rename the temporary file to the original.
        /// 2. Appending :
        /// Append updates to the new original WIM file, then overwrite its header such that those changes become visible to new readers.
        /// 3. Compaction : 
        /// Normally should not be used; see WriteFlags.UNSAFE_COMPACT for details.
        ///
        /// Append mode is often much faster than a full rebuild, but it wastes some amount of space due to leaving "holes" in the WIM file.
        /// Because of the greater efficiency, Wim.Overwrite() normally defaults to append mode.
        /// However, WriteFlags.REBUILD can be used to explicitly request a full rebuild.
        /// In addition, if Wim.DeleteImage() has been used on the WimStruct, then the default mode switches to rebuild mode, 
        /// and WriteFlags.SOFT_DELETE can be used to explicitly request append mode.
        ///
        /// If this function completes successfully, then no more functions can be called on the WimStruct other than Wim.Free().
        /// If you need to continue using the WIM file, you must use Wim.OpenWim() to open a new WimStruct for it.
        /// </remarks>
        /// <exception cref="WimLibException">wimlib did not return ErrorCode.SUCCESS.</exception>
        public void Overwrite(WriteFlags writeFlags, uint numThreads)
        {
            ErrorCode ret = NativeMethods.Overwrite(Ptr, writeFlags, numThreads);
            WimLibException.CheckWimLibError(ret);
        }
        #endregion

        #region Existence Check (ManagedWimLib Only)
        /// <summary>
        /// Check if a file exists in wim
        /// </summary>
        /// <param name="image">
        /// The 1-based index of the image that contains wimFilePath, or WimLibConst.ALL_IMAGES to iterate over all images.
        /// </param>
        /// <param name="wimFilePath">
        /// Path of file in the wim image.
        /// </param>
        /// <returns></returns>
        public bool FileExists(int image, string wimFilePath)
        {
            bool exists = false;
            CallbackStatus ExistCallback(DirEntry dentry, object userData)
            {
                if ((dentry.Attributes & FileAttribute.DIRECTORY) == 0)
                    exists = true;

                return CallbackStatus.CONTINUE;
            }

            try
            {
                this.IterateDirTree(image, wimFilePath, IterateFlags.DEFAULT, ExistCallback, null);
                return exists;
            }
            catch (WimLibException e) when (e.ErrorCode == ErrorCode.PATH_DOES_NOT_EXIST)
            {
                return false;
            }
        }

        /// <summary>
        /// Check if a file exists in wim
        /// </summary>
        /// <param name="image">
        /// The 1-based index of the image that contains wimDirPath, or WimLibConst.ALL_IMAGES to iterate over all images.
        /// </param>
        /// <param name="wimDirPath">
        /// Path of directory in the wim image.
        /// </param>
        /// <returns></returns>
        public bool DirExists(int image, string wimDirPath)
        {
            bool exists = false;
            CallbackStatus ExistCallback(DirEntry dentry, object userData)
            {
                if ((dentry.Attributes & FileAttribute.DIRECTORY) != 0)
                    exists = true;

                return CallbackStatus.CONTINUE;
            }

            try
            {
                this.IterateDirTree(image, wimDirPath, IterateFlags.DEFAULT, ExistCallback, null);
                return exists;
            }
            catch (WimLibException e) when (e.ErrorCode == ErrorCode.PATH_DOES_NOT_EXIST)
            {
                return false;
            }
        }

        /// <summary>
        /// Check if a file exists in wim
        /// </summary>
        /// <param name="image">
        /// The 1-based index of the image that contains wimPath, or WimLibConst.ALL_IMAGES to iterate over all images.
        /// </param>
        /// <param name="wimPath">
        /// Path of file or directory in the wim image.
        /// </param>
        /// <returns></returns>
        public bool PathExists(int image, string wimPath)
        {
            try
            {
                this.IterateDirTree(image, wimPath, IterateFlags.DEFAULT, null, null);
                return true;
            }
            catch (WimLibException e) when (e.ErrorCode == ErrorCode.PATH_DOES_NOT_EXIST)
            {
                return false;
            }
        }
        #endregion
    }
    #endregion

    #region StringHelper
    internal class StringHelper
    {
        public static string ReplaceEx(string str, string oldValue, string newValue, StringComparison comp)
        {
            if (oldValue.Equals(string.Empty, comp))
                return str;

            if (str.IndexOf(oldValue, comp) != -1)
            {
                int idx = 0;
                StringBuilder b = new StringBuilder();
                while (idx < str.Length)
                {
                    int vIdx = str.IndexOf(oldValue, idx, comp);

                    if (vIdx == -1)
                    {
                        b.Append(str.Substring(idx));
                        break;
                    }
                    else
                    {
                        b.Append(str.Substring(idx, vIdx - idx));
                        b.Append(newValue);
                        idx = vIdx += oldValue.Length;
                    }
                }
                return b.ToString();
            }
            else
            {
                return str;
            }
        }
    }
    #endregion
}
