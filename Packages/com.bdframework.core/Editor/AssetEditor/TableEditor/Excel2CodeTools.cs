﻿using System.CodeDom;
using System.CodeDom.Compiler;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using BDFramework.ResourceMgr;
using LitJson;
using UnityEditor;
using UnityEngine;
using System;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Text.RegularExpressions;
using BDFramework.Core.Tools;


namespace BDFramework.Editor.TableData
{
    static public class Excel2CodeTools
    {
        [MenuItem("BDFrameWork工具箱/3.表格/表格->生成Class", false, (int) BDEditorMenuEnum.BuildPackage_Table_Table2Class)]
        public static void Gen()
        {
            var ret = EditorUtility.DisplayDialog("提示", @"
Excel格式如下:
1.第一行为备注
2.第二行可以自定义字段类型，如没检测到则自动分析字段类型
3.所有表格字段名必须以Id开始，即第二或第三行首列必须是Id", "OK");
            if (ret)
            {
                GenCode();
            }
        }


        /// <summary>
        /// 生成代码
        /// </summary>
        public static void GenCode()
        {
            var xlslFiles = Excel2SQLiteTools.GetAllConfigFiles();
            //
            if (xlslFiles.Count == 0)
            {
                EditorUtility.DisplayDialog("提示", "未发现xlsx文件，请注意不是xls", "确定");
                return;
            }

            //同名文件判断
            var fnlist = xlslFiles.Select((s) => Path.GetFileName(s).ToLower()).ToList();

            foreach (var fn in fnlist)
            {
                var rets = fnlist.FindAll((f) => f == fn);
                if (rets.Count > 1)
                {
                    EditorUtility.DisplayDialog("提示", "Sqlite表名 字段名不区分大小写,请处理重名exel! ->" + fn, "OK");

                    return;
                }
            }


            foreach (var f in xlslFiles)
            {
                GenClassByExcel(f, "Local");
                GenClassByExcel(f, "Server");
            }

            EditorUtility.DisplayDialog("提示", "生成完成!", "确定");
            AssetDatabase.Refresh();
        }


        /// <summary>
        /// 通过excel生成class
        /// </summary>
        /// <param name="filename"></param>
        static private void GenClassByExcel(string filename, string dbType)
        {
            Debug.LogFormat("[{0}]正在生成：" + filename, dbType);
            var          excel         = new ExcelUtility(filename);
            int          idX           = -1;
            int          idY           = -1;
            List<object> keepFieldList = new List<object>();
            string       json          = excel.GetJson(dbType, ref idX, ref idY, ref keepFieldList);
            if (idX != -1 && idY != -1)
            {
                if (idY < 2)
                {
                    Debug.LogErrorFormat("【生成失败】 {0} ,请检查表头预留3行:备注，类型，字段名!"  ,Path.GetFileName(filename));
                    return;
                }

                //这里将前三列进行判断
                var statements = excel.GetRowDatas(idY - 2);
                var fieldTypes = excel.GetRowDatas(idY - 1);
                if (idX > 0)
                {
                    statements.RemoveRange(0, idX);
                    fieldTypes.RemoveRange(0, idX);
                    if (keepFieldList.Count > 0)
                    {
                        keepFieldList.RemoveRange(0, idX);
                    }
                }

                if (keepFieldList.Count > 0)
                {
                    for (int i = keepFieldList.Count - 1; i >= 0; i--)
                    {
                        if (!keepFieldList[i].Equals("*"))
                        {
                            statements.RemoveAt(i);
                            fieldTypes.RemoveAt(i);
                        }
                    }
                }
                
                Json2Class(filename, json, dbType, statements, fieldTypes);
                Debug.LogFormat("<color=red> [{0} 成功] </color>：{1}", dbType, filename);
            }
            else
            {
                Debug.LogError("不符合规范内容:" + filename);
            }
        }

        public class GenCodeTool
        {
            public string CalssContent = @"
//------------------------------------------------------------------------------
// <auto-generated>
//    Genera by BDFramework
// </auto-generated>
//------------------------------------------------------------------------------

namespace Game.Data.[NameSpace]
{
    using System;
    using System.Collections.Generic;
    using SQLite4Unity3d;
    
    [Serializable()]
    public class [ClsName]
    {
        [Fields]
    }
}";

            public string FieldContent = @"
        /// <summary>
        /// [Statement]
        /// </summary>
        [Attribute]
        public [Type] [Name] {get;set;}";

            public string FieldContent2 = @"
        /// <summary>
        /// [Statement]
        /// </summary>
        public [Type] [Name] {get;set;}";

            private string clsName = "Noname";

            public GenCodeTool(string clsName)
            {
                this.clsName = clsName;
            }


            private string fieldContents = "";

            /// <summary>
            /// 添加字段
            /// </summary>
            /// <param name="statement">注释</param>
            /// <param name="attribute">属性</param>
            /// <param name="type">类型</param>
            /// <param name="fieldName">字段名</param>
            public void AddField(string statement, string attribute, string type, string fieldName)
            {
                string tempfield = "";
                if (string.IsNullOrEmpty(attribute))
                {
                    tempfield = this.FieldContent2;
                }
                else
                {
                    tempfield = this.FieldContent;
                }

                tempfield = tempfield.Replace("[Statement]", statement);
                if (!string.IsNullOrEmpty(attribute))
                {
                    tempfield = tempfield.Replace("[Attribute]", $"[{attribute}]");
                }

                tempfield = tempfield.Replace("[Type]", type);
                tempfield = tempfield.Replace("[Name]", fieldName);

                this.fieldContents += tempfield;
            }


            /// <summary>
            /// 保存
            /// </summary>
            /// <param name="filePath"></param>
            public void Save(string filePath, string NameSpace)
            {
                this.CalssContent = CalssContent.Replace("[ClsName]", clsName) //
                    .Replace("[Fields]", fieldContents);
                this.CalssContent = this.CalssContent.Replace("[NameSpace]", NameSpace);
                FileHelper.WriteAllText(filePath, this.CalssContent);
            }
        }


        /// <summary>
        /// Json2Class
        /// 自动分析字段
        /// </summary>
        /// <param name="fileName"></param>
        /// <param name="json"></param>
        /// <param name="statements"></param>
        private static void Json2Class(string fileName, string json, string NameSpace, List<object> statements, List<object> fieldTypes)
        {
            string clsName = "";
            clsName = Path.GetFileNameWithoutExtension(fileName);
            //输出目录控制
            string outputFile = "Assets/Code/Game@hotfix/Table/" + NameSpace;
            if (Directory.Exists(outputFile) == false)
            {
                Directory.CreateDirectory(outputFile);
            }
            //输出目录
            outputFile = Path.Combine(outputFile, clsName + ".cs");
            //生成类服务
            GenCodeTool genCodeTool = new GenCodeTool(clsName);
            var         jsonData    = JsonMapper.ToObject(json)[0];
            int         i           = 0;
            foreach (var key in jsonData.Keys)
            {
                //字段
                string attribute = "";
                if (key.ToLower() == "id" && key != "Id")
                {
                    Debug.LogErrorFormat("<color=yellow>表格{0}字段必须为Id[大小写区分],请修改后生成</color>", clsName);
                    break;
                }
                else if (key == "Id")
                {
                    //增加一个sqlite主键
                    attribute = "PrimaryKey";
                }

                string type = null;
                if (fieldTypes != null && fieldTypes.Count >= jsonData.Count)
                {
                    type = fieldTypes[i].ToString();
                }

                //添加字段
                genCodeTool.AddField(statements[i].ToString(), attribute, type, key);
                i++;
            }

            //生成代码       
            genCodeTool.Save(outputFile, NameSpace);
        }


        [MenuItem("Assets/单个excel生成class", true)]
        private static bool SingleExcel2ClassValidation()
        {
            if (Selection.activeObject == null) return false;
            string path = AssetDatabase.GetAssetPath(Selection.activeObject);
            if (!path.EndsWith(".xlsx")) return false;
            return true;
        }

        [MenuItem("Assets/单个excel生成class")]
        public static void SingleExcel2Class()
        {
            string path = AssetDatabase.GetAssetPath(Selection.activeObject);
            GenClassByExcel(path, "Local");
            GenClassByExcel(path, "Server");
            AssetDatabase.Refresh();
        }
    }
}