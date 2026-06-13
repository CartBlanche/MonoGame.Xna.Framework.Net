// Copyright Epic Games, Inc. All Rights Reserved.

#if DEBUG
	#define EOS_DEBUG
#endif

#if UNITY_EDITOR
	#define EOS_EDITOR
#endif

#if UNITY_EDITOR || UNITY_STANDALONE || UNITY_IOS || UNITY_ANDROID
	#define EOS_UNITY
#endif

#if UNITY_EDITOR_WIN
	#define EOS_PLATFORM_WINDOWS_64
#elif UNITY_STANDALONE_WIN
	#if UNITY_64
		#define EOS_PLATFORM_WINDOWS_64
	#else
		#define EOS_PLATFORM_WINDOWS_32
	#endif

#elif UNITY_EDITOR_OSX || UNITY_STANDALONE_OSX
	#define EOS_PLATFORM_OSX

#elif UNITY_EDITOR_LINUX || UNITY_STANDALONE_LINUX
	#define EOS_PLATFORM_LINUX

#elif UNITY_IOS || __IOS__
	#define EOS_PLATFORM_IOS

#elif UNITY_ANDROID || __ANDROID__
	#define EOS_PLATFORM_ANDROID

#endif

using System.Runtime.InteropServices;

namespace Epic.OnlineServices
{
	public sealed partial class Common
	{
		// Use a stable logical library name and resolve to platform-native filenames
		// through a DllImportResolver in the host integration layer.
		public const string LIBRARY_NAME = "EOSSDK";

		public const CallingConvention LIBRARY_CALLING_CONVENTION = CallingConvention.Cdecl;
	}
}