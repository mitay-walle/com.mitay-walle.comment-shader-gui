# CommentShaderGUI

Custom **ShaderGUI** for Unity that extends the standard material inspector.  
It solves core limitations of Unity’s built-in system:

- `MaterialProperryDrawer` attributes cannot take extra parameters.  
- You cannot apply multiple attributes to the same property
- You cannot combine some built-in attributes with custom `MaterialPropertyDrawer`.  

With this system, attributes, tooltips, and help messages are parsed directly from **comments above shader properties**.  

<img width="853" height="1075" alt="{757AC5C4-E4B0-4A1C-B527-09DC089DA357}" src="https://github.com/user-attachments/assets/d6c7173f-3854-4bc5-a950-9140586c38a8" />


---

## ✨ Features

- Write **HTML-formatted tooltips** using simple `//` comments.  
- Show **HelpBox messages** using `[HelpBox]` tag inside comments.  
- Support for **multiple attributes on the same property**.  
- Extend the system with **custom processors** for any kind of control.  
- Works with built-in attributes (`[Toggle]`, `[HDR]`, `[Enum]`, `[Header]`) at the same time.  

---

## 📝 Usage

### Tooltip
Any comment starting with `//` becomes a **tooltip** for the next property.

```shader
// Main particle texture (RGBA)
_MainTex("Main Texture", 2D) = "white" {}
```

➡ Hover over the property in the inspector to see the tooltip.

---

### HelpBox
Use `[HelpBox]` inside a comment to show a **message box** above the property.

```shader
// [HelpBox]Gradient is applied to luminance
_Gradient("Gradient", 2D) = "white" {}
```

---

### Combining Tooltip and HelpBox
Mix them in one line.  
Text before `[HelpBox]` → tooltip, text after → HelpBox.

```shader
// Gradient texture [HelpBox]Only active when UseGradient is enabled
_Gradient("Gradient", 2D) = "white" {}
```

---

### Attribute-like Tags
Special tags can be written inside comments to control rendering:  

```shader
// [Vector2] UV Scroll Speed (x = U, y = V)
_Scroll("Scroll Speed", Vector) = (0,0,0,0)

// [LogarithmicRange] Distortion intensity
_Strength("Distortion Strength", Range(0,1)) = 0.2
```

---

## ⚙️ Built-in Tags / Processors

| Tag                   | Description                                                                 |
|------------------------|-----------------------------------------------------------------------------|
| `[Vector2]`            | Hide ZW components of a `Vector`. Show only **X/Y float fields**.                               |
| `[Vector2:X,Y]`        | Same as above, but with **custom labels** for each field.                  |
| `[MiniTexture]`        | Draws a compact texture field (mini thumbnail).                            |
| `[EnableIf:Property]`  | Disable the property only if another float/int property is less than 0.5.               |
| `[HelpBox]`            | Shows a help message above the property.                                   |

---

## ➕ Adding a New Processor

All custom logic is handled by **Property Processors**.  
Each processor extends the abstract base class:

```csharp
public abstract class PropertyProcessor
{
    public abstract void Draw(MaterialEditor editor, MaterialProperty prop, PropertyInfo info, GUIContent content);
}
```

### Steps to add:
1. Create a new class extending `PropertyProcessor`.  
2. inplement `CanDraw` to filter your attribute by `fieldType`
3. In `ParseComment` use Regex to find your attribute, and fill `info.cuatomData` with parsed values
4. Implement the `Draw` method.  
5. 

---

### Example: Step Slider

```csharp
	private class StepSliderProcessor : PropertyProcessor
	{
		private static Regex regex = new Regex(@"\[StepSlider\(\s*([-+]?\d*\.?\d+)\s*\)\]");

		public override int ProcessingOrder => 10;

		public override bool CanProcess(MaterialProperty prop, PropertyInfo info, List<string> unityAttributes)
		{
			return (prop.type == MaterialProperty.PropType.Float || prop.type == MaterialProperty.PropType.Range) && info.customData.ContainsKey("stepSize");
		}

		public override void Draw(MaterialEditor editor, MaterialProperty prop, PropertyInfo info, GUIContent content)
		{
			if (info.customData.TryGetValue("stepSize", out var val))
			{
				float step = (float)val;
				EditorGUI.BeginChangeCheck();
				float newVal = EditorGUILayout.Slider(content, prop.floatValue, prop.rangeLimits.x, prop.rangeLimits.y);
				newVal = Mathf.Round(newVal / step) * step;

				if (EditorGUI.EndChangeCheck())
					prop.floatValue = newVal;
			}
			else
			{
				Debug.LogError("TryGetValue(stepSize) failed");
			}
		}

		public override string ParseComment(string comment, ref PropertyInfo info, ref string helpBox)
		{
			var match = regex.Match(comment);
			if (match.Success)
			{
				if (float.TryParse(match.Groups[1].Value, NumberStyles.Float, CultureInfo.InvariantCulture, out float step))
				{
					info.customData["stepSize"] = step;
					comment = regex.Replace(comment, "");
				}
				else
				{
					Debug.LogError("stepSize failed parse");
				}
			}

			return comment;
		}
	}
```

Shader usage:

```shader
// [StepSlider(0.25)]
_Smoothness("Smoothness", Range(0,1)) = 0.5
```

---

## 🚀 Summary

- Tooltips, HelpBoxes, and multiple attributes are written in **shader comments**.  
- No need for multiple `MaterialPropertyDrawer` hacks.  
- Easy to add **custom processors** for any specialized UI control.  
- Makes Unity’s material inspector **far more flexible and maintainable**.
