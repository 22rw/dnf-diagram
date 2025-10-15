Add-Type -AssemblyName System.Windows.Forms
Add-Type -AssemblyName System.Drawing
Add-Type -AssemblyName System.Text.RegularExpressions

$ui = New-Object System.Windows.Forms.Form
$ui.Width = 400
$ui.Height = 600
$ui.MinimumSize = $ui.Size
$ui.Text = "DNF zu Skizze"
$ui.StartPosition = [System.Windows.Forms.FormStartPosition]::CenterScreen

$dnfInput = New-Object System.Windows.Forms.TextBox
$dnfInput.Location = New-Object System.Drawing.Point -ArgumentList 5,5
$dnfInput.Size = New-Object System.Drawing.Size -ArgumentList 370,48
$ui.Controls.Add($dnfInput)

$dnfFormat = New-Object System.Text.RegularExpressions.Regex -ArgumentList @"
^(!?\((!?\w){1,}\)(\sv\s(?!$))?)*$
"@

$gBtn = New-Object System.Windows.Forms.Button
$gBtn.Location = New-Object System.Drawing.Point -ArgumentList 5,(10 + $dnfInput.Height)
$gBtn.Text = "Generate"
$gBtn.Add_Click({
    $dnf = $dnfInput.Text.Trim()
    if (-Not $dnfFormat.Match($dnf).Success) { return }
    $topGroups = $dnf.Split(" v ")

})
$ui.Controls.Add($gBtn)

$ui.ShowDialog()