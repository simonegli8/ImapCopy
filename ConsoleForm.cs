#if PackAsTool
using System;
using System.Collections;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;

namespace ImapCopy;


public class ConsoleSettings
{
	public int CursorX;
	public int CursorY;
	public bool CursorVisible;
	public ConsoleColor ForegroundColor;
	public ConsoleColor BackgroundColor;

	public static ConsoleSettings Save()
	{
		ConsoleSettings settings = new ConsoleSettings();
		if (ConsoleForm.IsWindows) settings.CursorVisible = Console.CursorVisible;
		settings.CursorX = Console.CursorLeft;
		settings.CursorY = Console.CursorTop;
		settings.BackgroundColor = Console.BackgroundColor;
		settings.ForegroundColor = Console.ForegroundColor;
		return settings;
	}

	public void Restore()
	{
		CursorX = Math.Max(0, Math.Min(CursorX, Console.WindowWidth - 1));
		CursorY = Math.Max(0, Math.Min(CursorY, Console.WindowHeight - 1));

		Console.SetCursorPosition(CursorX, CursorY);
		Console.BackgroundColor = BackgroundColor;
		Console.ForegroundColor = ForegroundColor;

		if (ConsoleForm.IsWindows)
			Console.CursorVisible = CursorVisible;
	}

	public static ConsoleSettings Set(int X, int Y, bool visible, ConsoleColor backgroundColor, ConsoleColor foregroundColor)
	{
		var con = Save();
		X = Math.Max(0, Math.Min(X, Console.WindowWidth - 1));
		Y = Math.Max(0, Math.Min(Y, Console.WindowHeight - 1));

		Console.SetCursorPosition(X, Y);
		Console.BackgroundColor = backgroundColor;
		Console.ForegroundColor = foregroundColor;
		if (ConsoleForm.IsWindows) Console.CursorVisible = visible;
		return con;
	}
}

public class ConsoleField
{
	public string? Name = null;
	public int X { get; set; }
	public int Y { get; set; }
	public int Width { get; set; }
	public int Height { get; set; }
	public int CursorX { get; set; }
	public int CursorY { get; set; }
	public int WindowX { get; set; } = 0;
	public int WindowY { get; set; } = 0;
	public virtual bool Centered => false;
	public bool HasFocus;
	public virtual bool CanFocus => false;
	public virtual bool Editable => false;
	protected string text = "";
	public virtual string Text
	{
		get => text;
		set
		{
			if (text != value)
			{
				text = value;
				Validate?.Invoke(this);
			}
		}
	}
	public virtual string DisplayText => Text;

	public ConsoleField(ConsoleForm form = null) { Parent = form; Text = ""; }
	public Action<ConsoleField> Validate = null;
	public Action Click = null;
	public bool Clicked { get; set; } = false;
	public virtual bool Checked { get; set; } = false;
	public bool Visible => Parent?.Visible == true;
	public ConsoleColor BackgroundColor { get; set; } = ConsoleColor.Black;
	public ConsoleColor ForegroundColor { get; set; } = ConsoleColor.White;
	public virtual ConsoleForm Parent { get; set; } = null;

	public virtual bool Edit(ConsoleKeyInfo key) => false;
	public virtual void ReceiveFocus()
	{
		if (CanFocus) HasFocus = true;
	}

	public virtual void Show()
	{
		if (Visible)
		{
			// add space fillers to lines so Console.Write will cover the whole window
			var displayText = Regex.Replace(DisplayText, "(?<=^|\r?\n)([^\r\n$]*)(?=\r?\n|$)", match =>
			{
				var text = match.Groups[1].Value;
				int len = text.Length;
				int addLeft, addRight;
				if (!Centered)
				{
					addLeft = 0;
					addRight = Math.Max(0, Width - len + WindowX);
				}
				else
				{
					var w = Math.Max(0, Width - len);
					addLeft = w / 2;
					addRight = w - addLeft;
				}
				char[] spacesLeft = Enumerable.Repeat(' ', addLeft).ToArray();
				char[] spacesRight = Enumerable.Repeat(' ', addRight).ToArray();
				text = $"{new string(spacesLeft)}{text}{new string(spacesRight)}";
				if (WindowX < text.Length)
				{
					int length = Math.Min(Width, text.Length - WindowX);
					text = text.Substring(WindowX, length);
				}
				else
				{
					text = "";
				}
				return text;
			}, RegexOptions.Singleline);

			// save settings
			var con = ConsoleSettings.Set(X, Parent!.Y + Y, false, BackgroundColor, ForegroundColor);
			Console.Write(displayText);

			// restore settings
			con.Restore();
			if (HasFocus)
			{
				int x = Math.Max(0, Math.Min(X + CursorX - WindowX, Console.WindowWidth - 1));
				int y = Math.Max(0, Math.Min(Parent!.Y + Y + CursorY - WindowY, Console.WindowHeight - 1));

				Console.SetCursorPosition(x, y);
			}
		}
	}
}

public class PercentField : ConsoleField
{
	public PercentField(ConsoleForm form = null) : base(form)
	{
		BackgroundColor = ConsoleColor.DarkGray;
	}

	float _value = 0;
	public float Value
	{
		get { return _value; }
		set
		{
			if (_value != value)
			{
				_value = value;
				Show();
			}
		}
	}

	public override bool Centered => false;
	public int EstimatedMaxProgress { get; set; }
	int lines = 0;
	public int Lines
	{
		get => lines;
		set
		{
			lines = value;
			Value = 1.0f - (float)Math.Exp(-((float)lines / (float)EstimatedMaxProgress));
		}
	}

	private void ReportLines(string msg) => Lines++;

	public override void Show()
	{
		if (Visible)
		{
			var num = $"{Value:P0}";
			var pos = Math.Max(0, (Width - num.Length) / 2);
			var leftSpaces = new string(Enumerable.Repeat(' ', pos).ToArray());
			var rightSpacesCount = Math.Max(0, Width - pos - num.Length);
			var rightSpaces = new string(Enumerable.Repeat(' ', rightSpacesCount).ToArray());
			Text = $"{leftSpaces}{num}{rightSpaces}";
			pos = Math.Min(Width, Math.Max(0, (int)(Value * Width + 0.5)));
			var width = Width;
			var background = BackgroundColor;
			var x = X;
			Width = pos;
			BackgroundColor = ConsoleColor.Green;
			base.Show();
			BackgroundColor = background;
			Width = width - pos;
			WindowX = pos;
			X = x + pos;
			base.Show();
			Width = width;
			WindowX = 0;
			X = x;
		}
	}
}
public class TextField : ConsoleField
{
	public TextField(ConsoleForm form = null) : base(form)
	{
		BackgroundColor = ConsoleColor.DarkGray;
		ForegroundColor = ConsoleColor.Black;
	}
	public override bool CanFocus => true;
	public override bool Editable => true;
	public override void ReceiveFocus()
	{
		base.ReceiveFocus();
		CursorX = Math.Min(Width + WindowX - 1, Text.Length);
	}
    public override string Text
	{
		get => base.Text;
		set
		{
			if (base.Text != value)
			{
				base.Text = value;
				Show();
			}
		}
	}
	public override bool Edit(ConsoleKeyInfo key)
	{
		int pos;
		string text;
		switch (key.Key)
		{
			case ConsoleKey.UpArrow:
			case ConsoleKey.DownArrow:
			case ConsoleKey.Tab:
			case ConsoleKey.Enter:
				return Parent!.EditNavigate(key);
			case ConsoleKey.LeftArrow:
				CursorX = CursorX - 1;
				if (CursorX < 0)
				{
					CursorX = 0;
					if (WindowX > 0) WindowX--;
				}
				Show();
				break;
			case ConsoleKey.RightArrow:
				CursorX++;
				if (WindowX + CursorX > Text.Length) CursorX--;
				if (CursorX >= Width)
				{
					CursorX = Width - 1;
					if (WindowX <= Text.Length - Width) WindowX++;
				}
				Show();
				break;
			case ConsoleKey.Delete:
				pos = WindowX + CursorX;
				text = Text;
				if (pos < text.Length)
				{
					text = $"{text.Substring(0, pos)}{text.Substring(pos + 1)}";
					Text = text;
				}
				break;
			case ConsoleKey.Backspace:
				pos = WindowX + CursorX;
				if (pos > 0)
				{
					text = Text;
					text = $"{text.Substring(0, pos - 1)}{text.Substring(pos)}";
					base.Text = text;
				}
				if (CursorX > 0) CursorX--;
				else if (WindowX > 0) WindowX--;
				Show();
				break;
			default:
				text = Text;
				pos = CursorX + WindowX;
				base.Text = $"{text.Substring(0, pos)}{key.KeyChar}{text.Substring(pos)}";
				CursorX++;
				if (CursorX >= Width)
				{
					CursorX = Width - 1;
					if (WindowX <= Text.Length - Width) WindowX++;
				}
				Show();
				break;
		}
		return false;
	}

	public override void Show()
	{
		if (Visible)
		{
			base.Show();
			if (HasFocus)
			{
				int x = Math.Max(0, Math.Min(X + CursorX, Console.WindowWidth - 1));
				int y = Math.Max(0, Math.Min(Parent!.Y + Y, Console.WindowHeight - 1));

				Console.SetCursorPosition(x, y);
				if (ConsoleForm.IsWindows) Console.CursorVisible = true;
			}
		}
	}
}

public class LabelField : TextField
{
	public LabelField(ConsoleForm form= null): base(form)
	{
		BackgroundColor = form?.BackgroundColor ?? ConsoleColor.Black;
		ForegroundColor = form?.ForegroundColor ?? ConsoleColor.White;
	}

	public override bool Editable => false;
	public override bool CanFocus => false;
}
public class PasswordField : TextField
{
	public PasswordField(ConsoleForm form = null) : base(form) { }
	bool recursive = false;

	public override string DisplayText => new string('*', Text.Length);
}

public class Button : ConsoleField
{
	public bool Default { get; set; } = false;
	public override bool CanFocus => true;
	public override bool Centered => true;
	public Button(ConsoleForm form =  null) : base(form) { }
	public override string DisplayText
	{
		get
		{
			var text = Text.Trim(' ', '[', ']');

			int w = Math.Max(0, Width - text.Length - 2);
			int left = w / 2;
			int right = w - left;

			return $"[{new string(' ', left)}{text}{new string(' ', right)}]";
		}
	}
	public override void Show()
	{
		if (Visible)
		{
			var background = BackgroundColor;
			if (HasFocus) BackgroundColor = ConsoleColor.DarkGray;

			base.Show();

			BackgroundColor = background;
		}
	}
}

public class Choice : Button
{
	public Choice(ConsoleForm form = null) : base(form) { Checked = false; }
	public override bool Editable => true;

	public override void ReceiveFocus()
	{
		HasFocus = true;
	}

	public override bool Checked
	{
		get => !string.IsNullOrWhiteSpace(Text);
		set => Text = value ? "x" : "";
	}
	public override bool Edit(ConsoleKeyInfo key)
	{
		if (key.Key == ConsoleKey.Spacebar)
		{
			Checked = !Checked;
			Show();
			return false;
		}
		else return Parent!.EditNavigate(key);
	}
}
public class FieldList : KeyedCollection<string, ConsoleField>
{
	protected override string GetKeyForItem(ConsoleField item) => item.Name?.Trim() ?? "";
}

public class ConsoleForm
{
	public int Y = 0;
	public FieldList Fields = new FieldList();
	public static bool IsWindows => RuntimeInformation.IsOSPlatform(OSPlatform.Windows);
	public static bool IsMac => RuntimeInformation.IsOSPlatform(OSPlatform.OSX);
	public static bool IsLinux => RuntimeInformation.IsOSPlatform(OSPlatform.Linux);
	public string Template = "";
	int visible = 0;
	public bool Visible { get => visible == 1; set => visible = value ? 1 : 0; }
	public ConsoleColor BackgroundColor { get; set; } = Console.BackgroundColor;
	public ConsoleColor ForegroundColor { get; set;} = Console.ForegroundColor; 
	public static ConsoleForm Current { get; private set; }
	public static Stack<ConsoleForm> Stack { get; private set; } = new Stack<ConsoleForm>();
	public CancellationTokenSource Cancel { get; private set; } = new CancellationTokenSource(); 
	public PercentField Progress => Fields
		.OfType<PercentField>()
		.FirstOrDefault();

	public ConsoleField Focus = null;

	public LabelField Label => Fields
		.OfType<LabelField>()
		.FirstOrDefault();

	public Button DefaultButton => Fields
		.OfType<Button>()
		.FirstOrDefault(f => f.Default);

	public ConsoleField this[string name] => Fields[name];
	public ConsoleField this[int index] => Fields[index];
	public ConsoleForm Show()
	{
		Cancel?.Dispose();
		Cancel = new CancellationTokenSource();

		Visible = true;
		Current = this;
		ConsoleSettings.Set(0, 0, false, BackgroundColor, ForegroundColor);
		Console.Clear();        
		// count lines in Template
		var template = Template;
		int nlines = template.Length > 0 ? 1 : 0;
		var nl = template.IndexOf('\n');
		while (nl >= 0)
		{
			nlines++;
			nl = template.IndexOf("\n", nl + 1);

		}
		if (template.Length > 0 && template[template.Length - 1] == '\n') nlines--; 
		Y = (Console.WindowHeight - nlines) / 2;
		if (Y < 0) {
			/*var lines = template.Split('\n')
				//.Skip(-Y)
				.Take(Console.WindowHeight);
			template = string.Join("\n", lines);*/
			Y = 0;
        }
		if (ConsoleForm.IsWindows) Console.CursorVisible = false;
		Console.SetCursorPosition(0, Y);
		Console.Write(template);
		
		//SetFocus(Fields.FirstOrDefault(f => f.CanFocus));

		foreach (var field in Fields)
			field.Show();

		return this;
	}
	public ConsoleForm ShowDialog()
	{
		Show();
		Edit();
		Current = null;
		return this;
	}

    public async Task<ConsoleForm> ShowDialogAsync()
    {
        Show();
		await EditAsync();
        Current = null;
        return this;
    }

	public ConsoleForm OpenPopup()
	{
        Stack.Push(Current);
		Current?.Visible = false;
		return this;
    }
    public ConsoleForm ShowPopup()
	{
		OpenPopup();
		return Show();
	}

	public ConsoleForm ShowPopupDialog()
	{
        OpenPopup();
        ShowDialog();
		Close();
		return this;
	}
	public async Task<ConsoleForm> ShowPopupDialogAsync()
	{
        OpenPopup();
        await ShowDialogAsync();
        Close();
        return this;
    }

    public ConsoleForm Apply(Action<ConsoleForm> action)
	{
		action?.Invoke(this);
		return this;
	}
	public void SetFocus(ConsoleField field)
	{
		if (ConsoleForm.IsWindows) Console.CursorVisible = false;
		if (Focus != null)
		{
			Focus.HasFocus = false;
			Focus.Show();
		}
		if (field != null)
		{
			field.ReceiveFocus();
			Focus = field;
			field.Show();
		}
	}

	public Action Edited;
	public ConsoleForm Edit()
	{
		if (Focus == null) return this;

		do
		{
			var key = Console.ReadKey();
			if (Focus.Editable)
			{
				if (Focus.Edit(key)) return this;
			}
			else if (EditNavigate(key)) return this;

			Edited?.Invoke();
		} while (true);
		//return this;
	}

    public async Task<ConsoleForm> EditAsync()
    {
        if (Focus == null) return this;

        do
        {
			try
			{
				while (!Console.KeyAvailable && !Cancel.IsCancellationRequested) await Task.Delay(10, Cancel.Token);
			} catch (OperationCanceledException)
			{
				Cancel = new CancellationTokenSource();
				return this;
			}

			if (Cancel.IsCancellationRequested)
			{
				Cancel = new CancellationTokenSource();
				return this;
			}

			var key = Console.ReadKey();
            if (Focus.Editable)
            {
                if (Focus.Edit(key)) return this;
            }
            else if (EditNavigate(key)) return this;

            Edited?.Invoke();
        } while (true);
        //return this;
    }


    public bool EditNavigate(ConsoleKeyInfo key)
	{
		switch (key.Key)
		{
			case ConsoleKey.UpArrow:
			case ConsoleKey.DownArrow:
			case ConsoleKey.LeftArrow:
			case ConsoleKey.RightArrow:
			case ConsoleKey.Tab:
				EditChangeFocus(key);
				break;
			case ConsoleKey.Enter:
				if (Focus is Button)
				{
					Focus.Clicked = true;
					Focus.Click?.Invoke();
					return true;
				}
				else
				{
					var defaultButton = DefaultButton;
					if (defaultButton != null)
					{
						defaultButton.Clicked = true;
						defaultButton.Click?.Invoke();
						return true;
					}
				}
				break;
			default:
				Console.SetCursorPosition(Math.Max(0, Console.CursorLeft - 1), Console.CursorTop);
				Focus.Show();
				break;
		}
		return false;
	}

	public void EditChangeFocus(ConsoleKeyInfo key)
	{
		if (Focus == null) return;

		switch (key.Key)
		{
			case ConsoleKey.UpArrow:
				var fieldUpwards = Fields
					.Where(f => f.CanFocus && f.Y + f.Height / 2 < Focus.Y + Focus.Height / 2)
					.OrderByDescending(f => f.Y + f.Height / 2)
					.ThenBy(f => Math.Abs(f.X + f.Width / 2 - Focus.X - Focus.Width / 2))
					.FirstOrDefault();
				if (fieldUpwards != null) SetFocus(fieldUpwards);
				break;
			case ConsoleKey.DownArrow:
				var fieldDownwards = Fields
					.Where(f => f.CanFocus && f.Y + f.Height / 2 > Focus.Y + Focus.Height / 2)
					.OrderBy(f => f.Y + f.Height / 2)
					.ThenBy(f => Math.Abs(f.X + f.Width / 2 - Focus.X - Focus.Width / 2))
					.FirstOrDefault();
				if (fieldDownwards != null) SetFocus(fieldDownwards);
				break;
			case ConsoleKey.LeftArrow:
				var fieldLeftwards = Fields
					.Where(f => f.CanFocus && f.X + f.Width / 2 < Focus.X + Focus.Width / 2)
					.OrderBy(f => Math.Abs(f.Y + f.Height / 2 - Focus.Y - Focus.Height / 2))
					.ThenByDescending(f => f.X + f.Width / 2)
					.FirstOrDefault();
				if (fieldLeftwards != null) SetFocus(fieldLeftwards);
				break;
			case ConsoleKey.RightArrow:
				var fieldRightwards = Fields
					.Where(f => f.CanFocus && f.X + f.Width / 2 > Focus.X + Focus.Width / 2)
					.OrderBy(f => Math.Abs(f.Y + f.Height / 2 - Focus.Y - Focus.Height / 2))
					.ThenBy(f => f.X + f.Width / 2)
					.FirstOrDefault();
				if (fieldRightwards != null) SetFocus(fieldRightwards);
				break;
			case ConsoleKey.Tab:
				var index = Fields.IndexOf(Focus);
				if (key.Modifiers.HasFlag(ConsoleModifiers.Shift))
				{
					index = (index - 1 + Fields.Count) % Fields.Count;
					while (!Fields[index].CanFocus) index = (index - 1 + Fields.Count) % Fields.Count;
				}
				else
				{
					index = (index + 1) % Fields.Count;
					while (!Fields[index].CanFocus) index = (index + 1) % Fields.Count;
				}
				SetFocus(Fields[index]);
				break;
			default: break;
		}
	}

	bool closed = false;
	public ConsoleForm Close()
	{
		if (Interlocked.Exchange(ref visible, 0) == 1)
		{
			closed = true;
			Current = Stack.Count > 0 ? Stack.Pop() : null;

			Cancel.Cancel();

			if (Current == null || Current.closed)
			{
				Console.BackgroundColor = BackgroundColor;
				Console.Clear();
			}
			else Current.Show();
		}
		else closed = true;
        return this;
    }

    public ConsoleForm Save(object result)
	{
		if (result == null) return this;

		var type = result.GetType();
		foreach (var p in type.GetProperties())
		{
			if (Fields.Contains(p.Name))
			{
				var text = Fields[p.Name].Text;
				if (p.PropertyType == typeof(string))
				{
					p.SetValue(result, text);
				}
				else if (p.PropertyType == typeof(int))
				{
					int i = 0;
					if (int.TryParse(text, out i)) p.SetValue(result, i);
				}
				else if (p.PropertyType == typeof(long))
				{
					long i = 0;
					if (long.TryParse(text, out i)) p.SetValue(result, i);
				}
				else if (p.PropertyType == typeof(float))
				{
					float x = 0;
					if (float.TryParse(text, out x)) p.SetValue(result, x);
				}
				else if (p.PropertyType == typeof(double))
				{
					double x = 0;
					if (double.TryParse(text, out x)) p.SetValue(result, x);
				}
				else if (p.PropertyType == typeof(bool))
				{
					p.SetValue(result, Fields[p.Name].Checked);
				}
			}
		}
		foreach (var f in type.GetFields())
		{
			if (Fields.Contains(f.Name))
			{
				var text = Fields[f.Name].Text;
				if (f.FieldType == typeof(string))
				{
					f.SetValue(result, text);
				}
				else if (f.FieldType == typeof(int))
				{
					int i = 0;
					if (int.TryParse(text, out i)) f.SetValue(result, i);
				}
				else if (f.FieldType == typeof(long))
				{
					long i = 0;
					if (long.TryParse(text, out i)) f.SetValue(result, i);
				}
				else if (f.FieldType == typeof(float))
				{
					float x = 0;
					if (float.TryParse(text, out x)) f.SetValue(result, x);
				}
				else if (f.FieldType == typeof(double))
				{
					double x = 0;
					if (double.TryParse(text, out x)) f.SetValue(result, x);
				}
				else if (f.FieldType == typeof(bool))
				{
					f.SetValue(result, Fields[f.Name].Checked);
				}
			}
		}
		return this;
	}

	public ConsoleForm Load(object source)
	{
		if (source == null) return this;

		var type = source.GetType();
		foreach (var p in type.GetProperties())
		{
			if (Fields.Contains(p.Name))
			{
				var field = Fields[p.Name];
				if (p.PropertyType == typeof(bool)) field.Checked = (bool)p.GetValue(source);
				else
				{
					var text = p.GetValue(source)?.ToString();
					if (!string.IsNullOrEmpty(text)) field.Text = text!;
				}
			}
		}
		foreach (var f in type.GetFields())
		{
			if (Fields.Contains(f.Name))
			{
				var field = Fields[f.Name];
				if (f.FieldType == typeof(bool)) field.Checked = (bool)f.GetValue(source);
				else
				{
					var text = f.GetValue(source)?.ToString();
					if (!string.IsNullOrEmpty(text)) field.Text = text!;
				}
			}
		}
		return this;
	}

	public ConsoleForm() { }
	public ConsoleForm(string template) : this()
	{
		Parse(template);
	}

	public string Wrap(string template)
	{
		// Wrap lines that are longer than console width
		var sb = new StringBuilder();

		foreach (var line in template.Split('\n'))
		{
			string remaining = line;

			while (remaining.Length > Console.WindowWidth)
			{
				// Find the last space within the current console width.
				int breakPos = remaining.LastIndexOf(' ', Console.WindowWidth - 1);

				// No suitable space? Hard-wrap.
				if (breakPos < 0)
					breakPos = Console.WindowWidth;

				if (sb.Length > 0) sb.AppendLine();
				sb.Append(remaining.Substring(0, breakPos));

				remaining = remaining.Substring(breakPos);

				// Skip spaces at the beginning of the next line.
				remaining = remaining.TrimStart(' ');
			}

			if (sb.Length > 0) sb.AppendLine();
			sb.Append(remaining);
		}

		return sb.ToString();
	}
	public string Trim(string template)
	{
		template = Regex.Replace(template, @"(?:\[(?:\?|!|%|@)\s*[a-zA-Z_][a-zA-Z0-9_]*)|(?:(?<=\[)\*)(?=[^\n\]]*\])|(?<=\[(?:\?|!|%|@)[^\]]*)\]", "", RegexOptions.Singleline);
		return Regex.Replace(template, @"\[x\s*[A-Za-z_][A-Za-z_0-9]*\]", "[ ]");
	}
	public ConsoleForm Parse(string template)
	{
		template = Wrap(template.Trim());
		int n = 0;
		var fieldMatches = Regex.Matches(template, @"(?<=^(?<prefix>.*?))\[(?<option>\*|%|\?|\!|x|@|)(?:(?<!\[|\[\*)\s*(?<name>[A-Za-z_][A-Za-z_0-9]*))?(?<text>[^\]]*?)\]", RegexOptions.Singleline);
		var fields = fieldMatches
			.OfType<Match>()
			.Select<Match, ConsoleField>(m =>
			{
				int y = 0, x = 0;
				string prefix = "";
				if (m.Groups["prefix"].Success) prefix = Trim(m.Groups["prefix"].Value);
				int nl = prefix.IndexOf('\n');
				int lastnl = 0;
				while (nl > 0)
				{
					y++;
					lastnl = nl;
					nl = prefix.IndexOf('\n', lastnl + 1);
				}
				x = prefix.Length - lastnl - 1;

				var text = m.Groups["text"].Value;
				string name = "";
				if (m.Groups["name"].Success) name = m.Groups["name"].Value;
				else name = text.Trim();
				if (string.IsNullOrEmpty(name)) name = $"_noname{n++}";
				var option = m.Groups["option"].Value;
				switch (option)
				{
					case "*":
						return new Button(this) { Name = name, Default = true, X = x, Y = y, Width = text.Length + 2, Height = 1, Text = text.Trim() };
					case "":
					default:
						return new Button(this) { Name = name, X = x, Y = y, Width = text.Length + 2, Height = 1, Text = text.Trim() };
					case "?":
						return new TextField(this) { Name = name, X = x, Y = y, Width = text.Length, Height = 1, Text = text.Trim() };
					case "!":
						return new PasswordField(this) { Name = name, X = x, Y = y, Width = text.Length, Height = 1, Text = text.Trim() };
					case "%":
						return new PercentField(this) { Name = name, X = x, Y = y, Width = text.Length, Height = 1, Value = 0 };
					case "x":
						return new Choice(this) { Name = name, X = x, Y = y, Width = 3, Height = 1, Text = " " };
					case "@":
                        return new LabelField(this) { Name = name, X = x, Y = y, Width = text.Length, Height = 1, Text = text.Trim() };
                }
            });

		Fields.Clear();
		foreach (var field in fields)
		{
			Fields.Add(field);
		}

		if (DefaultButton == null)
		{
			var last = Fields.OfType<Button>().LastOrDefault();
			if (last != null) last.Default = true;
		}

		Focus = null;
		SetFocus(Fields.FirstOrDefault(f => f.CanFocus));

		Template = Trim(template);
		return this;
	}
}
#endif