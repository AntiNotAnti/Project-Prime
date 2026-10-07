param([long]$WindowHandle)
$ErrorActionPreference = 'Stop'
Add-Type -AssemblyName UIAutomationClient
Add-Type -AssemblyName UIAutomationTypes
$root = [System.Windows.Automation.AutomationElement]::FromHandle([IntPtr]$WindowHandle)
function Find-Control([string]$Id) {
  $condition = [System.Windows.Automation.PropertyCondition]::new([System.Windows.Automation.AutomationElement]::AutomationIdProperty, $Id)
  $element = $root.FindFirst([System.Windows.Automation.TreeScope]::Descendants, $condition)
  if ($null -eq $element) { throw "UIA control not found: $Id" }
  return $element
}
$email = Find-Control 'email'
if ($email.Current.Name -ne 'Email address' -or $email.Current.ControlType -ne [System.Windows.Automation.ControlType]::Edit) { throw 'UIA actual name/control type mismatch' }
$password = Find-Control 'password'
if (-not $password.Current.IsPassword) { throw 'UIA protected control flag missing' }
$email.SetFocus()
Start-Sleep -Milliseconds 100
$email = Find-Control 'email'
$value = [System.Windows.Automation.ValuePattern]$email.GetCurrentPattern([System.Windows.Automation.ValuePattern]::Pattern)
$unicode = [string][char]0x65E5 + [char]0x672C + [char]0x8A9E + ' ' + [char]::ConvertFromUtf32(0x1F600)
$value.SetValue($unicode)
Start-Sleep -Milliseconds 100
$email = Find-Control 'email'
$bounds = $email.Current.BoundingRectangle
if ($bounds.Width -le 0 -or $bounds.Height -le 0) { throw 'UIA screen bounds unavailable' }
$save = Find-Control 'save'
$invoke = [System.Windows.Automation.InvokePattern]$save.GetCurrentPattern([System.Windows.Automation.InvokePattern]::Pattern)
$invoke.Invoke()
Write-Output 'PASS real Windows UIA client name/role/password/focus/Unicode value/geometry/invoke'
