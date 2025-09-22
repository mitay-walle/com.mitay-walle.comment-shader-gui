using UnityEngine;
using UnityEditor;
using System;
using System.Collections.Generic;
using System.Text.RegularExpressions;
using System.Reflection;
using System.Linq;

public class CommentShaderGUI : ShaderGUI
{
	private static List<PropertyProcessor> processors;

	static CommentShaderGUI()
	{
		// Автоматическая регистрация всех процессоров через TypeCache
		var processorTypes = TypeCache.GetTypesDerivedFrom<PropertyProcessor>();
		processors = new List<PropertyProcessor>();

		foreach (var type in processorTypes)
		{
			if (!type.IsAbstract)
			{
				try
				{
					var processor = (PropertyProcessor)Activator.CreateInstance(type);
					processors.Add(processor);
				}
				catch (Exception e)
				{
					Debug.LogWarning($"Failed to create processor {type.Name}: {e.Message}");
				}
			}
		}

		// Сортируем по приоритету (меньшее значение = выше приоритет)
		processors.Sort((a, b) => a.ProcessingOrder.CompareTo(b.ProcessingOrder));
	}

	private Dictionary<string, PropertyInfo> propertyInfo = new Dictionary<string, PropertyInfo>();
	private List<string> unityAttributes = new List<string>();
	private Material lastMaterial;

	public override void OnGUI(MaterialEditor materialEditor, MaterialProperty[] properties)
	{
		Material mat = materialEditor.target as Material;
		if (propertyInfo.Count == 0 || lastMaterial != mat)
		{
			propertyInfo.Clear();
			ParseShaderComments(mat);
			lastMaterial = mat;
		}

		foreach (var prop in properties)
		{
			DrawProperty(materialEditor, prop);
		}

		materialEditor.RenderQueueField();
		materialEditor.EnableInstancingField();
		materialEditor.DoubleSidedGIField();
	}

	private void DrawProperty(MaterialEditor editor, MaterialProperty prop)
	{
		if (!propertyInfo.TryGetValue(prop.name, out var info))
		{
			// Если информации о свойстве нет — рисуем стандартным способом
			editor.ShaderProperty(prop, prop.displayName);
			return;
		}

		if (!string.IsNullOrEmpty(info.helpBox))
		{
			EditorGUILayout.HelpBox(info.helpBox, MessageType.Info);
		}

		GUIContent content = new GUIContent(prop.displayName, info.tooltip);

		bool enabled = string.IsNullOrEmpty(info.enableIfProperty) || CheckEnableIf(prop, info.enableIfProperty);

		EditorGUI.BeginDisabledGroup(!enabled);

		PropertyProcessor processor = GetProcessor(prop, info);
		processor.Draw(editor, prop, info, content);

		EditorGUI.EndDisabledGroup();
	}

	private bool CheckEnableIf(MaterialProperty prop, string dependency)
	{
		foreach (var target in prop.targets)
		{
			if (target is Material mat)
			{
				if (mat.HasProperty(dependency))
				{
					if (mat.GetFloat(dependency) <= 0f)
						return false;
				}
				else
				{
					return false;
				}
			}
		}

		return true;
	}

	private PropertyProcessor GetProcessor(MaterialProperty prop, PropertyInfo info)
	{
		// Ищем подходящий процессор
		foreach (var processor in processors)
		{
			if (processor.CanProcess(prop, info, unityAttributes))
				return processor;
		}

		return new DefaultProcessor();
	}

	private void ParseShaderComments(Material material)
	{
		if (material == null || material.shader == null) return;

		string path = AssetDatabase.GetAssetPath(material.shader);
		if (string.IsNullOrEmpty(path)) return;

		string[] lines = System.IO.File.ReadAllLines(path);

		for (int i = 0; i < lines.Length; i++)
		{
			string line = lines[i].Trim();
			if ((line.StartsWith("_") || line.StartsWith("[")) && line.Contains("("))
			{
				PropertyInfo info = new PropertyInfo();
				string propertyLine = line;

				while (propertyLine.StartsWith("["))
				{
					int close = propertyLine.IndexOf(']');
					if (close > 0)
						propertyLine = propertyLine.Substring(close + 1).Trim();
					else break;
				}

				int paren = propertyLine.IndexOf('(');
				if (paren <= 0) continue;

				string propName = propertyLine.Substring(0, paren).Trim();
				info.name = propName;

				string tooltip = "";
				string helpBox = "";
				unityAttributes.Clear();

				for (int j = i - 1; j >= 0; j--)
				{
					string prevLine = lines[j].Trim();
					if (string.IsNullOrEmpty(prevLine)) continue;

					if (prevLine.StartsWith("//"))
						ParseCommentAttributes(prevLine.Substring(2).Trim(), ref info, ref tooltip, ref helpBox);
					else if (prevLine.StartsWith("[") && prevLine.EndsWith("]"))
						ParseInlineAttribute(prevLine.Substring(1, prevLine.Length - 2));
					else break;
				}

				info.tooltip = tooltip.Trim();
				info.helpBox = helpBox.Trim();

				propertyInfo[propName] = info;
			}
		}
	}

	private void ParseCommentAttributes(string comment, ref PropertyInfo info, ref string tooltip, ref string helpBox)
	{
		string remainingComment = comment;

		// Обрабатываем все процессоры
		foreach (var processor in processors)
		{
			remainingComment = processor.ParseComment(remainingComment, ref info, ref helpBox);
		}

		tooltip += remainingComment.Trim() + "\n";
	}

	private void ParseInlineAttribute(string attr)
	{
		if (attr.StartsWith("HDR")) unityAttributes.Add("HDR");
		if (attr.StartsWith("Toggle")) unityAttributes.Add("Toggle");
		if (attr.StartsWith("Enum")) unityAttributes.Add("Enum");
		if (attr.StartsWith("Space")) unityAttributes.Add("Space");
	}

	public class PropertyInfo
	{
		public string name;
		public string tooltip;
		public string helpBox;
		public string enableIfProperty;
		public bool isMiniTexture;
		public bool isVector2;
		public string vector2XName = "X";
		public string vector2YName = "Y";
		public Type enumType;
		public bool isSpace;
		public float spaceHeight;

		// Дополнительные параметры для расширения
		public Dictionary<string, object> customData = new Dictionary<string, object>();
	}

	public abstract class PropertyProcessor
	{
		/// <summary>
		/// Порядок обработки процессора. Меньшее значение = выше приоритет.
		/// Default должен иметь самый высокий номер (обрабатывается последним).
		/// </summary>
		public virtual int ProcessingOrder => 100;

		public abstract bool CanProcess(MaterialProperty prop, PropertyInfo info, List<string> unityAttributes);
		public abstract void Draw(MaterialEditor editor, MaterialProperty prop, PropertyInfo info, GUIContent content);

		public virtual string ParseComment(string comment, ref PropertyInfo info, ref string helpBox)
		{
			return comment;
		}
	}

	private class HelpBoxProcessor : PropertyProcessor
	{
		private static Regex helpBoxRegex = new Regex(@"\[HelpBox\](?<text>.*)");

		public override int ProcessingOrder => 0; // Высший приоритет

		public override bool CanProcess(MaterialProperty prop, PropertyInfo info, List<string> unityAttributes)
		{
			return false; // HelpBox обрабатывается отдельно в DrawProperty
		}

		public override void Draw(MaterialEditor editor, MaterialProperty prop, PropertyInfo info, GUIContent content)
		{
			// Не используется
		}

		public override string ParseComment(string comment, ref PropertyInfo info, ref string helpBox)
		{
			Match m = helpBoxRegex.Match(comment);
			if (m.Success)
			{
				helpBox += m.Groups["text"].Value.Trim() + "\n";
				comment = helpBoxRegex.Replace(comment, "");
			}

			return comment;
		}
	}

	private class EnableIfProcessor : PropertyProcessor
	{
		private static Regex enableIfRegex = new Regex(@"\[EnableIf:(?<prop>[^\]]+)\]");

		public override int ProcessingOrder => 1;

		public override bool CanProcess(MaterialProperty prop, PropertyInfo info, List<string> unityAttributes)
		{
			return false; // EnableIf обрабатывается отдельно в DrawProperty
		}

		public override void Draw(MaterialEditor editor, MaterialProperty prop, PropertyInfo info, GUIContent content)
		{
			// Не используется
		}

		public override string ParseComment(string comment, ref PropertyInfo info, ref string helpBox)
		{
			Match m = enableIfRegex.Match(comment);
			if (m.Success)
			{
				info.enableIfProperty = m.Groups["prop"].Value.Trim();
				comment = enableIfRegex.Replace(comment, "");
			}

			return comment;
		}
	}

	private class MiniTextureProcessor : PropertyProcessor
	{
		private static Regex miniTextureRegex = new Regex(@"\[MiniTexture\]");

		public override int ProcessingOrder => 10;

		public override bool CanProcess(MaterialProperty prop, PropertyInfo info, List<string> unityAttributes)
		{
			return prop.type == MaterialProperty.PropType.Texture && info.isMiniTexture;
		}

		public override void Draw(MaterialEditor editor, MaterialProperty prop, PropertyInfo info, GUIContent content)
		{
			editor.TexturePropertyMiniThumbnail(EditorGUILayout.GetControlRect(), prop, content.text, content.tooltip);
		}

		public override string ParseComment(string comment, ref PropertyInfo info, ref string helpBox)
		{
			if (miniTextureRegex.IsMatch(comment))
			{
				info.isMiniTexture = true;
				comment = miniTextureRegex.Replace(comment, "");
			}

			return comment;
		}
	}

	private class Vector2Processor : PropertyProcessor
	{
		private static Regex vector2Regex = new Regex(@"\[Vector2(?::(?<x>[^,\]]+),(?<y>[^\]]+))?\]");

		public override int ProcessingOrder => 20;

		public override bool CanProcess(MaterialProperty prop, PropertyInfo info, List<string> unityAttributes)
		{
			return prop.type == MaterialProperty.PropType.Vector && info.isVector2;
		}

		public override void Draw(MaterialEditor editor, MaterialProperty prop, PropertyInfo info, GUIContent content)
		{
			Vector4 v = prop.vectorValue;

			Rect r = EditorGUILayout.GetControlRect();
			float totalWidth = r.width;
			float spacing = 4f;

			float oldLabelWidth = EditorGUIUtility.labelWidth;

			// ширина под главный лейбл (название свойства)
			Rect mainLabelRect = new Rect(r.x, r.y, totalWidth/3, r.height);

			// ширина под X/Y метки
			float xLabelWidth = EditorStyles.label.CalcSize(new GUIContent(info.vector2XName)).x + 4f;
			float yLabelWidth = EditorStyles.label.CalcSize(new GUIContent(info.vector2YName)).x + 4f;

			// оставшееся место делим на 2 поля
			float remainingWidth = totalWidth - mainLabelRect.width - xLabelWidth - yLabelWidth - spacing * 4f;
			float fieldWidth = remainingWidth / 2f;

			// Разметка
			Rect xLabelRect = new Rect(mainLabelRect.xMax + spacing, r.y, xLabelWidth, r.height);
			Rect xFieldRect = new Rect(xLabelRect.xMax + spacing, r.y, fieldWidth, r.height);
			Rect yLabelRect = new Rect(xFieldRect.xMax + spacing, r.y, yLabelWidth, r.height);
			Rect yFieldRect = new Rect(yLabelRect.xMax + spacing, r.y, fieldWidth, r.height);

			// Рисуем
			EditorGUI.LabelField(mainLabelRect, content);

			EditorGUI.BeginChangeCheck();
			float newX = EditorGUI.FloatField(xFieldRect, GUIContent.none, v.x);
			EditorGUI.LabelField(xLabelRect, info.vector2XName);

			float newY = EditorGUI.FloatField(yFieldRect, GUIContent.none, v.y);
			EditorGUI.LabelField(yLabelRect, info.vector2YName);

			if (EditorGUI.EndChangeCheck())
				prop.vectorValue = new Vector4(newX, newY, v.z, v.w);

			EditorGUIUtility.labelWidth = oldLabelWidth;
		}

		public override string ParseComment(string comment, ref PropertyInfo info, ref string helpBox)
		{
			Match m = vector2Regex.Match(comment);
			if (m.Success)
			{
				info.isVector2 = true;
				if (m.Groups["x"].Success && m.Groups["y"].Success)
				{
					info.vector2XName = m.Groups["x"].Value.Trim();
					info.vector2YName = m.Groups["y"].Value.Trim();
				}

				comment = vector2Regex.Replace(comment, "");
			}

			return comment;
		}
	}

	private class ToggleProcessor : PropertyProcessor
	{
		private static Regex toggleRegex = new Regex(@"\[Toggle\]");

		public override int ProcessingOrder => 30;

		public override bool CanProcess(MaterialProperty prop, PropertyInfo info, List<string> unityAttributes)
		{
			return (prop.type == MaterialProperty.PropType.Float || prop.type == MaterialProperty.PropType.Int) && unityAttributes.Contains("Toggle");
		}

		public override void Draw(MaterialEditor editor, MaterialProperty prop, PropertyInfo info, GUIContent content)
		{
			Rect r = EditorGUILayout.GetControlRect();
			bool val = prop.floatValue != 0f;
			EditorGUI.BeginChangeCheck();
			val = EditorGUI.Toggle(r, content, val);
			if (EditorGUI.EndChangeCheck())
				prop.floatValue = val ? 1f : 0f;
		}

		public override string ParseComment(string comment, ref PropertyInfo info, ref string helpBox)
		{
			// Обрабатывается через unityAttributes
			if (toggleRegex.IsMatch(comment))
			{
				comment = toggleRegex.Replace(comment, "");
			}

			return comment;
		}
	}

	private class EnumProcessor : PropertyProcessor
	{
		private static Regex enumRegex = new Regex(@"\[Enum\((?<type>[^\)]+)\)\]");

		public override int ProcessingOrder => 40;

		public override bool CanProcess(MaterialProperty prop, PropertyInfo info, List<string> unityAttributes)
		{
			return (prop.type == MaterialProperty.PropType.Float || prop.type == MaterialProperty.PropType.Int) && info.enumType != null;
		}

		public override void Draw(MaterialEditor editor, MaterialProperty prop, PropertyInfo info, GUIContent content)
		{
			if (info.enumType == null)
			{
				EditorGUILayout.HelpBox("Enum type not found", MessageType.Error);
				return;
			}

			Array names = Enum.GetValues(info.enumType);
			string[] options = Enum.GetNames(info.enumType);
			int[] values = new int[options.Length];
			for (int i = 0; i < options.Length; i++)
				values[i] = (int)names.GetValue(i);

			int idx = Array.IndexOf(values, (int)prop.floatValue);
			if (idx < 0) idx = 0;

			int sel = EditorGUILayout.Popup(content, idx, options);
			prop.floatValue = values[sel];
		}

		public override string ParseComment(string comment, ref PropertyInfo info, ref string helpBox)
		{
			Match m = enumRegex.Match(comment);
			if (m.Success)
			{
				try { info.enumType = Type.GetType(m.Groups["type"].Value.Trim(), false); }
				catch { }

				comment = enumRegex.Replace(comment, "");
			}

			return comment;
		}
	}

	private class SpaceProcessor : PropertyProcessor
	{
		private static Regex spaceRegex = new Regex(@"\[Space\]");

		public override int ProcessingOrder => 50;

		public override bool CanProcess(MaterialProperty prop, PropertyInfo info, List<string> unityAttributes)
		{
			return info.isSpace;
		}

		public override void Draw(MaterialEditor editor, MaterialProperty prop, PropertyInfo info, GUIContent content)
		{
			GUILayout.Space(info.spaceHeight);
		}

		public override string ParseComment(string comment, ref PropertyInfo info, ref string helpBox)
		{
			if (spaceRegex.IsMatch(comment))
			{
				info.isSpace = true;
				info.spaceHeight = 8f;
				comment = spaceRegex.Replace(comment, "");
			}

			return comment;
		}
	}

	private class HDRColorProcessor : PropertyProcessor
	{
		public override int ProcessingOrder => 60;

		public override bool CanProcess(MaterialProperty prop, PropertyInfo info, List<string> unityAttributes)
		{
			return prop.type == MaterialProperty.PropType.Color && unityAttributes.Contains("HDR");
		}

		public override void Draw(MaterialEditor editor, MaterialProperty prop, PropertyInfo info, GUIContent content)
		{
			Rect r = EditorGUILayout.GetControlRect();
			EditorGUI.BeginChangeCheck();
			Color newColor = EditorGUI.ColorField(r, content, prop.colorValue, true, true, true);
			if (EditorGUI.EndChangeCheck())
				prop.colorValue = newColor;
		}
	}

	private class DefaultProcessor : PropertyProcessor
	{
		public override int ProcessingOrder => 1000; // Самый низкий приоритет - обрабатывается последним

		public override bool CanProcess(MaterialProperty prop, PropertyInfo info, List<string> unityAttributes)
		{
			return true; // Всегда может обработать
		}

		public override void Draw(MaterialEditor editor, MaterialProperty prop, PropertyInfo info, GUIContent content)
		{
			editor.ShaderProperty(prop, content);
		}
	}
}
