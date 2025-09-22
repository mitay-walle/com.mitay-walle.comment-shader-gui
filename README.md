# CommentShaderGUI

Custom **ShaderGUI** for Unity that extends the standard material inspector.  
It solves core limitations of Unity’s built-in system:

- Attributes like `[Toggle]`, `[Enum]`, `[Header]` cannot take extra parameters.  
- You cannot apply multiple attributes to the same property.  
- You cannot combine built-in attributes with custom `MaterialPropertyDrawer`.  

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
| `[Vector2]`            | Splits a `Vector` into **X/Y float fields**.                               |
| `[Vector2:X,Y]`        | Same as above, but with **custom labels** for each field.                  |
| `[LogarithmicRange]`   | Makes `Range` slider respond **logarithmically** instead of linearly.       |
| `[MiniTexture]`        | Draws a compact texture field (mini thumbnail).                            |
| `[EnableIf:Property]`  | Shows the property only if another toggle property is `true`.               |
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
2. Implement the `Draw` method.  
3. Register the processor by inheriting `PropertyProcessor` (it is auto-discovered via `TypeCache`).  

---

### Example: Step Slider

```csharp
public class StepSliderProcessor : PropertyProcessor
{
    private float step;

    public StepSliderProcessor(float step) => this.step = step;

    public override void Draw(MaterialEditor editor, MaterialProperty prop, PropertyInfo info, GUIContent content)
    {
        EditorGUI.BeginChangeCheck();
        float newVal = EditorGUILayout.Slider(content, prop.floatValue, 0, 1);

        newVal = Mathf.Round(newVal / step) * step;

        if (EditorGUI.EndChangeCheck())
            prop.floatValue = newVal;
    }
}
```

Parser detection example:

```csharp
if (tag.StartsWith("StepSlider"))
{
    float step = 0.1f;
    var match = Regex.Match(tag, @"StepSlider\(([\d\.]+)\)");
    if (match.Success) step = float.Parse(match.Groups[1].Value);
    processor = new StepSliderProcessor(step);
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
