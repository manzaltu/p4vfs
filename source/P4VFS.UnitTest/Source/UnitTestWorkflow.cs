﻿// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.
using System;
using System.Diagnostics;
using System.Linq;
using System.IO;
using System.Text;
using System.Reflection;
using System.Text.RegularExpressions;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Microsoft.P4VFS.Extensions;
using Microsoft.P4VFS.Extensions.Utilities;
using Microsoft.P4VFS.CoreInterop;

namespace Microsoft.P4VFS.UnitTest
{
	[TestClass, BasePriority(2000)]
	public class UnitTestWorkflow : UnitTestBase
	{
		[TestMethod, Priority(0)]
		public void ReconciledTest()
		{
			foreach (ServiceSettingsScope settings in EnumerateCommonServicePopulateSettings())
			{
				using (settings) {
				WorkspaceReset();

				string[] fileSpecs = new string[]{ 
					"//depot/tools/dev/source/CinematicCapture/...@16",
					"//depot/tools/dev/source/CinematicCapture/...@17",
					"//depot/tools/dev/source/CinematicCapture/...@18",
					"//depot/tools/dev/source/Hammer/...@19",
					"//depot/tools/dev/source/Hammer/...@20",
					"//depot/tools/dev/source/Hammer/...@21",
					"//depot/tools/dev/external/EsrpClient/1.1.5/Enable-EsrpClientLog.ps1",
					"//depot/gears1/WarGame/Localization/INT/Human_Myrrah_Dialog.int",
					"//depot/tools/dev/source/...@=17",
					"//depot/tools/dev/source/...@16,20",
					"//depot/tools/dev/source/...@7,@18",
				};

				foreach (string fileSpec in fileSpecs)
				{
					Assert(ProcessInfo.ExecuteWait(P4vfsExe, String.Format("{0} sync {1}", ClientConfig, fileSpec), echo:true, log:true) == 0);
					Assert(ProcessInfo.ExecuteWait(P4Exe, String.Format("{0} flush -f {1}", ClientConfig, fileSpec), echo:true) == 0);
					Assert(ReconcilePreview(fileSpec.Split('@')[0]).Any() == false);
				}
			}}
		}

		[TestMethod, Priority(1)]
		public void CurrentWorkingDirectoryTest()
		{
			foreach (ServiceSettingsScope settings in EnumerateCommonServicePopulateSettings())
			{
				using (settings) {
				foreach (string syncOption in EnumerateCommonConsoleSyncOptions())
				{
					WorkspaceReset();

					string clientRoot = GetClientRoot();
					string currentDirectory = String.Format(@"{0}\depot\tools\dev\source\CinematicCapture", clientRoot);
					string revision = "@16";

					FileUtilities.CreateDirectory(currentDirectory);
					Assert(System.IO.Directory.Exists(currentDirectory));
					string lastCurrentDirectory = Environment.CurrentDirectory;
					Environment.CurrentDirectory = currentDirectory;

					Assert(ProcessInfo.ExecuteWait(P4vfsExe, String.Format("{0} sync {1} ...{2}", ClientConfig, syncOption, revision), echo:true, log:true) == 0);
					Assert(ProcessInfo.ExecuteWait(P4Exe, String.Format("{0} flush -f \"{1}\\...{2}\"", ClientConfig, currentDirectory, revision), echo:true) == 0);
					Assert(ReconcilePreview(currentDirectory).Any() == false);
					Environment.CurrentDirectory = lastCurrentDirectory;
				}
			}}
		}

		[TestMethod, Priority(2)]
		public void SyncProtocolsTest()
		{
			foreach (string syncOption in EnumerateCommonConsoleSyncOptions())
			{
				WorkspaceReset();

				string clientRoot = GetClientRoot();
				string directory = String.Format(@"{0}\depot\tools\dev\source\CinematicCapture", clientRoot);
				string revision = "@16";

				ServiceRestart();
				Assert(ProcessInfo.ExecuteWait(P4vfsExe, String.Format("{0} sync {1} \"{2}\\...{3}\"", ClientConfig, syncOption, directory, revision), echo:true, log:true) == 0);
				Assert(ProcessInfo.ExecuteWait(P4Exe, String.Format("{0} flush -f \"{1}\\...{2}\"", ClientConfig, directory, revision), echo:true) == 0);
				Assert(ReconcilePreview(directory).Any() == false);
				ServiceRestart();
			}
		}

		[TestMethod, Priority(3)]
		public void MonitorShowHideTest()
		{
			Func<int> GetMonitorProcessCount = () => Process.GetProcesses().Count(p => (p.ProcessName ?? "").IndexOf(VirtualFileSystem.MonitorTitle, StringComparison.InvariantCultureIgnoreCase) >= 0);
			Assert(VirtualFileSystem.HideMonitor());
			Assert(GetMonitorProcessCount() == 0);
			Assert(VirtualFileSystem.ShowMonitor());
			Assert(GetMonitorProcessCount() == 1);
			Assert(VirtualFileSystem.HideMonitor());
			Assert(GetMonitorProcessCount() == 0);
			Assert(ProcessInfo.ExecuteWait(P4vfsExe, "monitor show", echo:true, log:true) == 0);
			Assert(GetMonitorProcessCount() == 1);
			Assert(ProcessInfo.ExecuteWait(P4vfsExe, "monitor show", echo:true, log:true) == 0);
			Assert(GetMonitorProcessCount() == 1);
			Assert(ProcessInfo.ExecuteWait(P4vfsExe, "monitor hide", echo:true, log:true) == 0);
			Assert(GetMonitorProcessCount() == 0);
			Assert(ProcessInfo.ExecuteWait(P4vfsExe, "monitor hide", echo:true, log:true) == 0);
			Assert(GetMonitorProcessCount() == 0);
		}

		[TestMethod, Priority(4)]
		public void ServiceInstallUninstallTest()
		{
			Func<string, string[]> ExecuteSC = (string args) => { return ProcessInfo.ExecuteWaitOutput("sc.exe", args, log:true).Lines; };
			Assert(ExecuteSC(String.Format("query {0}", VirtualFileSystem.ServiceTitle)).Any(line => Regex.IsMatch(line, @"STATE\s+.+\s+RUNNING")));
			Assert(VirtualFileSystem.UninstallService());
			Assert(ExecuteSC(String.Format("query {0}", VirtualFileSystem.ServiceTitle)).Any(line => line.Contains("specified service does not exist")));
			Assert(VirtualFileSystem.UninstallService());
			Assert(ExecuteSC(String.Format("query {0}", VirtualFileSystem.ServiceTitle)).Any(line => line.Contains("specified service does not exist")));
			Assert(ProcessInfo.ExecuteWait(InstalledP4vfsExe, "install -s", echo:true, log:true) == 0);
			Assert(ExecuteSC(String.Format("query {0}", VirtualFileSystem.ServiceTitle)).Any(line => Regex.IsMatch(line, @"STATE\s+.+\s+RUNNING")));

			WorkspaceReset();
			StringBuilder syncOutput = new StringBuilder();
			System.Threading.Thread syncThread = new System.Threading.Thread(new System.Threading.ThreadStart(() => 
			{
				ProcessInfo.ExecuteWait(P4vfsExe, String.Format("{0} sync -s //depot/gears1/Development/...@343068", ClientConfig), echo:true, log:true, stdout:syncOutput);
			}));

			syncThread.Start();
			while (syncThread.IsAlive && syncOutput.Length < 1000)
				System.Threading.Thread.Sleep(100);

			Assert(syncThread.IsAlive && VirtualFileSystem.UninstallService());
			syncThread.Join();
			Assert(ExecuteSC(String.Format("query {0}", VirtualFileSystem.ServiceTitle)).Any(line => line.Contains("specified service does not exist")));
			Assert(ProcessInfo.ExecuteWait(InstalledP4vfsExe, "install -s", echo:true, log:true) == 0);
			Assert(ExecuteSC(String.Format("query {0}", VirtualFileSystem.ServiceTitle)).Any(line => Regex.IsMatch(line, @"STATE\s+.+\s+RUNNING")));
			WorkspaceReset();
		}

		[TestMethod, Priority(5), TestRemote]
		public void ResidentCommandTest()
		{
			foreach (ServiceSettingsScope settings in EnumerateCommonServicePopulateSettings())
			{
				using (settings) {
				WorkspaceReset();

				string clientRoot = GetClientRoot();
				string directory = String.Format(@"{0}\depot\tools\dev\source\CinematicCapture", clientRoot);
				string subdirectory = String.Format(@"{0}\Source", directory);
				string revision = "@16";

				DepotResultWhere whereDirectory = Where(directory);
				Assert(whereDirectory.Count == 1);
				Assert(String.Compare(directory, whereDirectory.First.LocalPath, true) == 0);
				string depotDirectory = whereDirectory.First.DepotPath;
				Assert(String.Compare(directory, Where(depotDirectory).First.LocalPath, true) == 0);

				Assert(ProcessInfo.ExecuteWait(P4vfsExe, String.Format("{0} sync \"{1}\\...{2}\"", ClientConfig, directory, revision), echo:true, log:true) == 0);

				string[] directoryFiles = Directory.GetFiles(directory, "*", SearchOption.AllDirectories).ToArray();
				Assert(directoryFiles.Length == 13);
				foreach (string filePath in directoryFiles)
				{
					Assert(File.Exists(filePath));
					Assert(IsPlaceholderFile(filePath));
				}

				Action assertReconcile = () =>
				{
					Assert(ProcessInfo.ExecuteWait(P4Exe, String.Format("{0} flush -f \"{1}\\...{2}\"", ClientConfig, directory, revision), echo:true) == 0);
					Assert(ReconcilePreview(directory).Any() == false);
					foreach (string filePath in directoryFiles)
					{
						Assert(File.Exists(filePath));
						Assert(IsPlaceholderFile(filePath) == false);
					}
				};

				Assert(ProcessInfo.ExecuteWait(P4vfsExe, String.Format("{0} resident \"{1}\\...\"", ClientConfig, directory), echo:true, log:true) == 0);
				foreach (string filePath in directoryFiles)
				{
					Assert(File.Exists(filePath));
					Assert(IsPlaceholderFile(filePath) == false);
				}

				assertReconcile();
				Assert(ProcessInfo.ExecuteWait(P4vfsExe, String.Format("{0} resident -v \"{1}\\...\"", ClientConfig, directory), echo:true, log:true) == 0);
				foreach (string filePath in directoryFiles)
				{
					Assert(File.Exists(filePath));
					Assert(IsPlaceholderFile(filePath));
				}

				Assert(ProcessInfo.ExecuteWait(P4vfsExe, String.Format("{0} resident -r -x \"cs,xml\" \"{1}\\...\"", ClientConfig, directory), echo:true, log:true) == 0);
				foreach (string filePath in directoryFiles)
				{
					Assert(File.Exists(filePath));
					Assert(IsPlaceholderFile(filePath) != Regex.IsMatch(filePath, @"(\.cs|\.xml)$", RegexOptions.IgnoreCase));
				}

				assertReconcile();
				Assert(ProcessInfo.ExecuteWait(P4vfsExe, String.Format("{0} resident -v -x \"cs,xml\" \"{1}\\...\"", ClientConfig, directory), echo:true, log:true) == 0);
				foreach (string filePath in directoryFiles)
				{
					Assert(File.Exists(filePath));
					Assert(IsPlaceholderFile(filePath) == Regex.IsMatch(filePath, @"(\.cs|\.xml)$", RegexOptions.IgnoreCase));
				}

				assertReconcile();
				Assert(ProcessInfo.ExecuteWait(P4vfsExe, String.Format("{0} resident -v \"{1}\\...\"", ClientConfig, subdirectory), echo:true, log:true) == 0);
				foreach (string filePath in directoryFiles)
				{
					Assert(File.Exists(filePath));
					Assert(IsPlaceholderFile(filePath) == filePath.StartsWith(subdirectory+"\\"));
				}

				assertReconcile();
				Assert(ProcessInfo.ExecuteWait(P4vfsExe, String.Format("{0} resident -v \"{1}/{2}/...\"", ClientConfig, depotDirectory, Path.GetFileName(subdirectory)), echo:true, log:true) == 0);
				foreach (string filePath in directoryFiles)
				{
					Assert(File.Exists(filePath));
					Assert(IsPlaceholderFile(filePath) == filePath.StartsWith(subdirectory+"\\"));
				}
			}
		}}

		[TestMethod, Priority(6), TestRemote]
		public void ServiceHostStatusTest()
		{
			WorkspaceReset();

			string clientRoot = GetClientRoot();
			Extensions.SocketModel.SocketModelClient client = new Extensions.SocketModel.SocketModelClient();
			Extensions.SocketModel.SocketModelReplyServiceStatus status0 = client.GetServiceStatus();
			Assert(status0 != null);
			Assert(status0.LastModifiedTime > DateTime.MinValue);
			Assert(status0.LastRequestTime > DateTime.MinValue);

			string fileLocalPath = String.Format(@"{0}\depot\gears1\Development\Src\Core\Src\Core.cpp", clientRoot);
			Assert(ProcessInfo.ExecuteWait(P4vfsExe, String.Format("{0} sync \"{1}\"", ClientConfig, fileLocalPath), echo:true, log:true) == 0);
			Assert(IsPlaceholderFile(fileLocalPath));
			
			Extensions.SocketModel.SocketModelReplyServiceStatus status1 = client.GetServiceStatus();
			Assert(status1 != null);
			Assert(status1.LastModifiedTime.AddSeconds(10) > DateTime.Now);
			Assert(status1.LastModifiedTime > status0.LastModifiedTime);
			Assert(status1.LastRequestTime == status0.LastRequestTime);

			Assert(ReconcilePreview(Path.GetDirectoryName(fileLocalPath)).Any() == false);
			Assert(IsPlaceholderFile(fileLocalPath) == false);

			Extensions.SocketModel.SocketModelReplyServiceStatus status2 = client.GetServiceStatus();
			Assert(status2 != null);
			Assert(status2.LastModifiedTime > status1.LastModifiedTime);
			Assert(status2.LastRequestTime > status1.LastRequestTime);
		}

		[TestMethod, Priority(7)]
		public void PopulateTest()
		{
			foreach (string syncOption in new[]{"", "-f"})
			{
				WorkspaceReset();

				string clientRoot = GetClientRoot();
				string directory = String.Format(@"{0}\depot\tools\dev\source\CinematicCapture", clientRoot);
				string revision = "@16";

				Assert(ProcessInfo.ExecuteWait(P4vfsExe, String.Format("{0} populate {1} \"{2}\\...{3}\"", ClientConfig, syncOption, directory, revision), echo:true, log:true) == 0);
				Assert(ProcessInfo.ExecuteWait(P4Exe, String.Format("{0} flush -f \"{1}\\...{2}\"", ClientConfig, directory, revision), echo:true) == 0);
				Assert(ReconcilePreview(directory).Any() == false);
			}
		}

		[TestMethod, Priority(8)]
		public void LegacyCompatibilityTest()
		{
			string legacySetupExeVariable = "P4VFS_TEST_LEGACY_SETUP";
			string legacySetupExe = Environment.GetEnvironmentVariable(legacySetupExeVariable);
			if (String.IsNullOrEmpty(legacySetupExe)) 
			{
				VirtualFileSystemLog.Warning("Skipping LegacyCompatibilityTest from missing required environment variable: {0}", legacySetupExeVariable);
				return;
			}

			string localLegacySetupFolder = String.Format("{0}\\legacy", UnitTestInstall.GetIntermediateRootFolder());
			AssertLambda(() => FileUtilities.DeleteDirectoryAndFiles(localLegacySetupFolder));
			VirtualFileSystemLog.Info("LegacyCompatibilityTest: {0}", legacySetupExe);

			Assert(File.Exists(legacySetupExe));
			Assembly legacySetupAssembly = Assembly.ReflectionOnlyLoadFrom(legacySetupExe);
			string[] resourceNames = legacySetupAssembly.GetManifestResourceNames();
			foreach (string resourceName in resourceNames) 
			{
				string fileName = Regex.Replace(resourceName, @"^P4VFS\.Setup\.(Resource\.)?", "");
				if (String.IsNullOrEmpty(fileName) || fileName.EndsWith(".resources", StringComparison.InvariantCultureIgnoreCase))
					continue;

				string filePath = Path.Combine(localLegacySetupFolder, fileName);
				VirtualFileSystemLog.Info("Extracting Legacy Resource: {0}", filePath);
				Assert(ExtractResourceToFile(resourceName, filePath, legacySetupAssembly));
			}

			string legacyP4vfsExe = Path.Combine(localLegacySetupFolder, "p4vfs.exe");
			Assert(File.Exists(legacyP4vfsExe));

			string[] fileSpecs = new string[]{ 
				"//depot/tools/dev/source/CinematicCapture/...@16",
				"//depot/tools/dev/source/CinematicCapture/...@17",
				"//depot/tools/dev/source/Hammer/...@19",
				"//depot/tools/dev/source/Hammer/...@20",
				"//depot/gears1/Binaries/Xenon/...",
			};

			WorkspaceReset();
			foreach (string fileSpec in fileSpecs)
			{
				// Only test locally performed sync's (sync -t) because current service is not compatible
				Assert(ProcessInfo.ExecuteWait(legacyP4vfsExe, String.Format("{0} sync -t {1}", ClientConfig, fileSpec), echo:true, log:true) == 0);
				Assert(ProcessInfo.ExecuteWait(P4Exe, String.Format("{0} flush -f {1}", ClientConfig, fileSpec), echo:true) == 0);
				Assert(ReconcilePreview(fileSpec.Split('@')[0]).Any() == false);
			}
		}

		[TestMethod, Priority(9)]
		public void SyncStatusTest()
		{
			var AssertFileSyncStatus = new Action<string,string,bool>((string depotSpec, string syncOption, bool valid) =>
			{
				int exitCode = ProcessInfo.ExecuteWait(P4vfsExe, String.Format("{0} sync {1} {2}", ClientConfig, syncOption, depotSpec), echo:true, log:true);
				Assert((valid == true && exitCode == 0) || (valid == false && exitCode != 0));
				ProcessInfo.ExecuteResultOutput flushOutput = ProcessInfo.ExecuteWaitOutput(P4Exe, String.Format("{0} flush -f {1}", ClientConfig, depotSpec), echo:true);
				exitCode = (exitCode == 0 && flushOutput.HasStdErr == false) ? 0 : 1;
				Assert((valid == true && exitCode == 0) || (valid == false && exitCode != 0));
				Assert(ReconcilePreview(Regex.Replace(depotSpec, @"[^/]+$", "...")).Any() == false);
			});

			foreach (string syncOption in EnumerateCommonConsoleSyncOptions())
			{
				WorkspaceReset();
				AssertFileSyncStatus("//depot/gears1/WarGame/Localization/INT/Human_Myrrah_Dialog.int#1", syncOption, true);
				WorkspaceReset();
				AssertFileSyncStatus("//depot/gears1/WarGame/Localization/INT/Human_Myrrah_Dialog.int#256", syncOption, false);
			}
		}
	}
}
