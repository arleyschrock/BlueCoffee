﻿using Microsoft.Experimental.Azure.CommonTestUtilities;
using Microsoft.Experimental.Azure.Hive;
using Microsoft.Experimental.Azure.JavaPlatform;
using Microsoft.Experimental.Azure.Spark;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace Microsoft.Experimental.Azure.Spark.Tests
{
	[TestClass]
	public class SharkNodeRunnerTest
	{
		private const string JavaHome = @"C:\Program Files\Java\jdk1.7.0_21";

		[TestMethod]
		[Ignore]
		public void EndToEndTest()
		{
			var tempDirectory = @"C:\SharkTestOutput";
			if (Directory.Exists(tempDirectory))
			{
				Directory.Delete(tempDirectory, recursive: true);
			}
			var hiveRoot = Path.Combine(tempDirectory, "HiveRoot");
			var sharkRoot = Path.Combine(tempDirectory, "Shark");
			var sparkRoot = Path.Combine(tempDirectory, "Spark");
			var killer = new ProcessKiller();
			var hiveRunner = SetupHive(hiveRoot);
			var metastoreConfig = new HiveDerbyMetastoreConfig(
				derbyDataDirectory: Path.Combine(hiveRoot, "metastore"),
				extraProperties: WasbProperties());
			var hiveTask = Task.Factory.StartNew(() => hiveRunner.RunMetastore(metastoreConfig, runContinuous: false, monitor: killer));
			var sparkRunner = SetupSpark(sparkRoot);
			var masterTask = Task.Factory.StartNew(() => sparkRunner.RunMaster(runContinuous: false, monitor: killer));
			var slaveTask = Task.Factory.StartNew(() => sparkRunner.RunSlave(runContinuous: false, monitor: killer));
			var sharkRunner = SetupShark(sharkRoot);
			var metastoreLogFile = Path.Combine(hiveRoot, "logs", "hive-metastore.log");
			ConditionAwaiter.WaitForLogSnippet(metastoreLogFile, "Initialized ObjectStore");
			var sharkTask = Task.Factory.StartNew(() => sharkRunner.RunSharkServer2(runContinuous: false, monitor: killer, debugPort: 1045));

			try
			{
				var sharkLogFile = Path.Combine(sharkRoot, "logs", "SharkLog.log");
				ConditionAwaiter.WaitForLogSnippet(sharkLogFile, "HiveThriftServer2 started");
				var dataFilePath = Path.Combine(tempDirectory, "TestData.txt");
				File.WriteAllText(dataFilePath, String.Join("\n", 501, 623, 713), Encoding.ASCII);
				var beelineOutput = sharkRunner.RunBeeline(new[]
				{
					"CREATE TABLE t(a int);",
					String.Format("LOAD DATA LOCAL INPATH 'file:///{0}' INTO TABLE t;", dataFilePath.Replace('\\', '/')),
					"SELECT * FROM t WHERE a > 700;",
					"SELECT * FROM t t1 JOIN t t2 on t1.a = t2.a;",
				});
				Trace.WriteLine("Stderr: " + beelineOutput.StandardError);
				StringAssert.Contains(beelineOutput.StandardOutput, "713");
			}
			finally
			{
				killer.KillAll();
				Task.WaitAll(hiveTask, sharkTask, masterTask, slaveTask);
			}
		}

		private static ImmutableDictionary<string, string> WasbProperties()
		{
			return new Dictionary<string, string>()
				{
					{ "fs.azure.skip.metrics", "true" },
					// Add account keys here.
				}.ToImmutableDictionary();
		}

		private static SharkRunner SetupShark(string sharkRoot)
		{
			var config = new SharkConfig(
				serverPort: 9444,
				metastoreUris: "thrift://localhost:9083",
				sparkMaster: "spark://localhost:7234",
				extraHiveConfig: WasbProperties());
			var runner = new SharkRunner(
				resourceFileDirectory: ResourcePaths.SparkResourcesPath,
				sharkHome: sharkRoot,
				javaHome: JavaHome,
				config: config);
			runner.Setup();
			return runner;
		}

		private static HiveRunner SetupHive(string hiveRoot)
		{
			var runner = new HiveRunner(
				resourceFileDirectory: ResourcePaths.HiveResourcesPath,
				jarsDirectory: Path.Combine(hiveRoot, "jars"),
				javaHome: JavaHome,
				logsDirctory: Path.Combine(hiveRoot, "logs"),
				configDirectory: Path.Combine(hiveRoot, "conf"));
			runner.Setup();
			return runner;
		}

		private static SparkRunner SetupSpark(string sparkRoot)
		{
			var config = new SparkConfig(
				masterAddress: "localhost",
				masterPort: 7234,
				masterWebUIPort: 7235,
				hadoopConfigProperties: WasbProperties());
			var runner = new SparkRunner(
				resourceFileDirectory: ResourcePaths.SparkResourcesPath,
				sparkHome: sparkRoot,
				javaHome: JavaHome,
				config: config);
			runner.Setup();
			return runner;
		}
	}
}
