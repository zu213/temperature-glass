using System;
using System.Runtime.InteropServices;
using System.Drawing;
using System.Windows.Forms;
using OpenHardwareMonitor.Hardware;
using System.Threading.Tasks;
using System.Diagnostics;

class TempGlass : Form
{
    private Panel drawingPanel;
    private Timer updateTimer;
    private string averageTempText = "0°C";
    private OpenHardwareMonitor.Hardware.Computer computer;
    private bool isDragging = false;
    private int offsetX = 0;

    // Constants for window styles
    private const int WS_EX_LAYERED = 0x00080000;
    private const int WS_EX_TOPMOST = 0x00000008;
    private const int WS_EX_TRANSPARENT = 0x00000020;
    private const int LWA_COLORKEY = 0x00000001;
    private const int LWA_ALPHA =    0x00000003;

    // Import Windows API functions
    [DllImport("user32.dll", SetLastError = true)]
    private static extern int SetWindowLong(IntPtr hWnd, int nIndex, int dwNewLong);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern int GetWindowLong(IntPtr hWnd, int nIndex);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool SetLayeredWindowAttributes(IntPtr hwnd, int crKey, byte bAlpha, int dwFlags);

    // decalre static for rounding
    [DllImport("Gdi32.dll", EntryPoint = "CreateRoundRectRgn")]
    private static extern IntPtr CreateRoundRect
        (
            int nLeftRect,     // x-coordinate of upper-left corner
            int nTopRect,      // y-coordinate of upper-left corner
            int nRightRect,    // x-coordinate of lower-right corner
            int nBottomRect,   // y-coordinate of lower-right corner
            int nWidthEllipse, // width of ellipse
            int nHeightEllipse // height of ellipse
        );

    // Override method to hide the form from alt-tab [https://www.csharp411.com/hide-form-from-alttab/]
    protected override CreateParams CreateParams
    {
        get
        {
            CreateParams cp = base.CreateParams;
            cp.ExStyle |= 0x80;
            return cp;
        }
    }

    public TempGlass()
    {
        this.FormBorderStyle = FormBorderStyle.None;
        this.ShowInTaskbar = false;
        this.Size = new Size(220, 35);
        this.TopMost = true;
        this.BackColor = Color.Blue;
        this.Load += new EventHandler(LoadWindow);
        this.Text = "Temperature Glass";
        this.StartPosition = FormStartPosition.Manual;
        Region = System.Drawing.Region.FromHrgn(CreateRoundRect(0, 0, Width, Height, 15, 15));

        // Set the position to the top middle of the screen
        int middle = Screen.PrimaryScreen.Bounds.Width / 2;
        this.Location = new Point(middle - 110, 0);


        // Add the panel witht he contents
        drawingPanel = new Panel
        {
            Size = new Size(220, 30),
            Location = new Point(0, 0),
            BackColor = Color.Blue
        };
        drawingPanel.Paint += new PaintEventHandler(DrawPanel); // Add paint callback
        this.Controls.Add(drawingPanel);

        // Add the computer information
        computer = new OpenHardwareMonitor.Hardware.Computer
        {
            CPUEnabled = true
        };
        computer.Open();

        // Adda  timer for async updates
        updateTimer = new Timer();
        updateTimer.Interval = 1000;
        updateTimer.Tick += UpdateTimer_Tick;
        updateTimer.Start();

        // add dragging event handlers
        drawingPanel.MouseDown += (sender, e) => { this.TempGlass_MouseDown(sender, e); };
        drawingPanel.MouseMove += (sender, e) => { this.TempGlass_MouseMove(sender, e); };
        drawingPanel.MouseUp += (sender, e) => { this.TempGlass_MouseUp(sender, e); };

    }

    // Event handler wrapper for async method call
    private async void UpdateTimer_Tick(object sender, EventArgs e)
    {
        await UpdateCPU_TemperatureAsync(); // Asynchronously update CPU temperature
    }

    // Async method to update CPU temperature
    private async Task UpdateCPU_TemperatureAsync()
    {
        await Task.Run(() =>
        {
            UpdateVisitor updateVisitor = new UpdateVisitor();
            computer.Accept(updateVisitor);

            foreach (IHardware hardware in computer.Hardware)
            {
                if (hardware.HardwareType == HardwareType.CPU)
                {
                    hardware.Update();
                    float total = 0;
                    int count = 0;
                    foreach (ISensor sensor in hardware.Sensors)
                    {
                        if (sensor.SensorType == SensorType.Temperature)
                        {
                            if(sensor.Value.HasValue)
                            {
                                total += (float)sensor.Value;
                                count++;
                            }
                        }
                    }
                    if (count == 0) count = 1;
                    float avg = total / count;
                    averageTempText = avg + "°C";
                    drawingPanel.Invalidate();
                }
            }
        });
    }

    // Dragging left and right events 
    private void TempGlass_MouseDown(object sender, MouseEventArgs e)
    {
        if (e.Button == MouseButtons.Left)
        {
            isDragging = true;
            offsetX = e.X;
        }
    }

    private void TempGlass_MouseMove(object sender, MouseEventArgs e)
    {
        if (isDragging)
        {
            // Only update the X position (Y stays the same)
            int newX = this.Left + (e.X - offsetX);
            this.Location = new System.Drawing.Point(newX, this.Top);
        }
    }

    private void TempGlass_MouseUp(object sender, MouseEventArgs e)
    {
        isDragging = false;
    }

    // Updating the panel when need be
    private void DrawPanel(object sender, PaintEventArgs e)
    {
        Graphics g = e.Graphics;
        string staticText = "Average core temperature: ";
        Font font = new Font("Roboto", 10);
        SizeF staticTextSize = g.MeasureString(staticText, font);
        g.Clear(Color.Blue);
        g.DrawString("Average core temperature: ", new Font("Roboto", 10), Brushes.White, new PointF(10, 10));
        g.DrawString(averageTempText, new Font("Roboto", 10), Brushes.White, new PointF(staticTextSize.Width + 1, 10));
    }

    // Processing creating the windows
    private void LoadWindow(object sender, EventArgs e)
    {
        IntPtr hWnd = this.Handle;
        int currentStyle = GetWindowLong(hWnd, -20);
        SetWindowLong(hWnd, -20, currentStyle | WS_EX_LAYERED | WS_EX_TOPMOST);
        SetLayeredWindowAttributes(hWnd, 128, 180, LWA_ALPHA); // Third parameter is transaprency
    }

    [STAThread]
    static void Main()
    {       
        System.Windows.Forms.Application.EnableVisualStyles();
        System.Windows.Forms.Application.Run(new TempGlass());
    }
    private void TempGlass_FormClosing(object sender, FormClosingEventArgs e)
    {
        computer.Close();
    }

    // Computer sub class for getting hardware info
    public class UpdateVisitor : IVisitor
    {
        public void VisitComputer(IComputer computer) { computer.Traverse(this); }
        public void VisitHardware(IHardware hardware)
        {
            hardware.Update();
            foreach (IHardware subHardware in hardware.SubHardware)
                subHardware.Accept(this);
        }
        public void VisitSensor(ISensor sensor) { }
        public void VisitParameter(IParameter parameter) { }
    }
}
