using System;
using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEditor.Build.Reporting;
using UnityEngine;

public class BuildCI : Editor
{
    private static readonly string Eol = Environment.NewLine;
    
    /// <summary>
    /// コンパイラーチェック、AutoUpdate用
    /// </summary>
    private static void CompileErrorCheckCMD()
    {
        Debug.Log("UnityVersion : " + Application.unityVersion );
        Debug.Log("Platform : " + EditorUserBuildSettings.activeBuildTarget + " is OK");
    }
    
    /// <summary>
    /// BatchModeからのクライアントビルド用
    /// </summary>
    private static void BuildClientCMD()
    {
        var nowTarget = EditorUserBuildSettings.activeBuildTarget;
        var buildPath = "build-" + nowTarget.ToString();
        switch (nowTarget)
        {
            case BuildTarget.Android:
                buildPath = buildPath + "/" + PlayerSettings.productName + (EditorUserBuildSettings.buildAppBundle ? ".aab" : ".apk");
                break;
            case BuildTarget.StandaloneWindows:
            case BuildTarget.StandaloneWindows64:
                buildPath = buildPath + "/" + PlayerSettings.productName + ".exe";
                break;
        }
        
        var buildPlayerOptions = new BuildPlayerOptions
        {
            scenes = EditorBuildSettings.scenes.Where(scene => scene.enabled).Select(s => s.path).ToArray(),
            target = nowTarget,
            locationPathName = buildPath,
        };

        var buildSummary = BuildPipeline.BuildPlayer(buildPlayerOptions).summary;
        ReportSummary(buildSummary);
        ExitWithResult(buildSummary.result);
    }

    private static void ReportSummary(BuildSummary summary)
    {
        Console.WriteLine(
            $"{Eol}" +
            $"###########################{Eol}" +
            $"#      Build results      #{Eol}" +
            $"###########################{Eol}" +
            $"{Eol}" +
            $"Duration: {summary.totalTime.ToString()}{Eol}" +
            $"Warnings: {summary.totalWarnings.ToString()}{Eol}" +
            $"Errors: {summary.totalErrors.ToString()}{Eol}" +
            $"Size: {summary.totalSize.ToString()} bytes{Eol}" +
            $"{Eol}"
        );
    }

    private static void ExitWithResult(BuildResult result)
    {
        switch (result)
        {
            case BuildResult.Succeeded:
                Console.WriteLine("Build succeeded!");
                EditorApplication.Exit(0);
                break;
            case BuildResult.Failed:
                Console.WriteLine("Build failed!");
                EditorApplication.Exit(101);
                break;
            case BuildResult.Cancelled:
                Console.WriteLine("Build cancelled!");
                EditorApplication.Exit(102);
                break;
            case BuildResult.Unknown:
            default:
                Console.WriteLine("Build result is unknown!");
                EditorApplication.Exit(103);
                break;
        }
    }
}