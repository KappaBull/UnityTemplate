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
        //CMDオプション定義
        const string exportPath = "-exportPath";
        string[] targetCmd = {exportPath};
        //CMD取得
        var cmd = Environment.GetCommandLineArgs();
        var maxCmdLength = cmd.Length;
        
        //CMDからデータ取得とパラメータ反映
        var buildPath = "";
        for (int i = 0; i < maxCmdLength; i++)
        {
            if (targetCmd.Any(c => cmd[i] == c))
            {
                switch (cmd[i])
                {
                    //シーンリスト編集とビルドオプションを変更
                    case exportPath:
                        buildPath = cmd[i+1];
                        break;
                }
            }
        } 
        
        //ビルド
        Debug.Log("BuildLocations : " + buildPath);
        Build(buildPath);
    }

    private static void Build(string filePath)
    {
        var scenes = EditorBuildSettings.scenes.Where(scene => scene.enabled).Select(s => s.path).ToArray();
        var buildPlayerOptions = new BuildPlayerOptions
        {
            scenes = scenes,
            target = EditorUserBuildSettings.activeBuildTarget,
            locationPathName = filePath,
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