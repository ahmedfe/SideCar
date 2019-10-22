﻿// Copyright (c) Daniel Crenna & Contributors. All rights reserved.
// Licensed under the Apache License, Version 2.0. See License.txt in the project root for license information.

using System.Runtime.CompilerServices;

namespace SideCar.Blazor
{
	internal static class Sequence
	{
		private static object _lastHost;

		internal static int Current
		{
			get;
			private set;
		}

		public static void Begin(int startingAt, object host)
		{
			Current = startingAt;
			_lastHost = host;
		}

		public static int NextSequence(this object host, [CallerLineNumber] int? lineNumber = null)
		{
			if (_lastHost != host)
				Current += 10000;
			_lastHost = host;
			Current += lineNumber.GetValueOrDefault();
			return Current;
		}

		public static int NextSequence([CallerLineNumber] int? lineNumber = null)
		{
			// ReSharper disable once ExplicitCallerInfoArgument
			return NextSequence(null, lineNumber);
		}
	}
}