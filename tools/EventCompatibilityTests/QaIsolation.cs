using System;
using System.Collections.Generic;

// Test-only managed PlayerPrefs replacement. Never shipped with the plugin.
public static class QaPrefs
{
    static readonly Dictionary<string,object> values = new Dictionary<string,object>();
    public static bool HasKey(string key) => values.ContainsKey(key);
    public static void DeleteKey(string key) { values.Remove(key); }
    public static void DeleteAll() { values.Clear(); }
    public static void Save() { }
    public static void SetInt(string key,int value) { values[key]=value; }
    public static void SetFloat(string key,float value) { values[key]=value; }
    public static void SetString(string key,string value) { values[key]=value; }
    public static bool TrySetInt(string key,int value) { SetInt(key,value);return true; }
    public static bool TrySetFloat(string key,float value) { SetFloat(key,value);return true; }
    public static bool TrySetSetString(string key,string value) { SetString(key,value);return true; }
    public static bool TrySetString(string key,string value) { SetString(key,value);return true; }
    public static int GetInt(string key) => GetInt(key,0);
    public static int GetInt(string key,int value) => values.TryGetValue(key,out var v) && v is int ? (int)v:value;
    public static float GetFloat(string key) => GetFloat(key,0);
    public static float GetFloat(string key,float value) => values.TryGetValue(key,out var v) && v is float ? (float)v:value;
    public static string GetString(string key) => GetString(key,"");
    public static string GetString(string key,string value) => values.TryGetValue(key,out var v) && v is string ? (string)v:value;
}
