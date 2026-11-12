export function getWindowsToolGuide(target: string, appName?: string) {
  const key = target.toLowerCase().replace(/[_-]/g, ' ');
  if (/reliability/.test(key)) return {
    name: 'Windows Reliability Monitor',
    steps: [
      ['Find the time', 'Choose the day when the problem happened and select its failure marker.'],
      ['Read the details', 'Match the app, time and error details to the problem you noticed. A record does not prove the cause.'],
      ['Focus on that app', 'If the same app keeps failing, check its official update or repair option. Then return and check for new records.'],
    ],
  };
  if (/installed apps|apps features|appsfeatures/.test(key)) return {
    name: 'Windows Installed apps',
    steps: [
      ['Find the app', appName ? `Search for “${appName}”. The next button tries to copy its name for you.` : 'Search for the app you chose in RigMD.'],
      ['Review its options', 'Check the publisher and exact name. Windows shows whether an uninstall option is available.'],
      ['Choose, then recheck', 'Save your work and confirm removal only if you no longer need the app. Return to RigMD and check storage again.'],
    ],
  };
  if (/storage/.test(key)) return {
    name: 'Windows Storage',
    steps: [
      ['Choose the drive', 'Match the drive letter to the storage warning in RigMD.'],
      ['Review a category', 'Compare installed apps, temporary files and your files. A large size does not mean an item is safe to remove.'],
      ['Review before removal', 'Choose only items you understand and no longer need. Return to RigMD and check available space again.'],
    ],
  };
  if (/device manager/.test(key)) return {
    name: 'Windows Device Manager',
    steps: [
      ['Find the device', 'Locate the device with the warning symbol.'],
      ['Read its status', 'Open its properties and note the device name and error code. The code is a clue, not a confirmed hardware fault.'],
      ['Use a specific fix', 'Check that device’s manufacturer guidance. Avoid changing unrelated devices, then return to RigMD and recheck.'],
    ],
  };
  if (/startup/.test(key)) return {
    name: 'Windows Startup apps',
    steps: [
      ['Review startup apps', 'Find apps that launch when you sign in.'],
      ['Understand the switch', 'Disabling startup stops automatic launch; it does not uninstall the app.'],
      ['Change only what you know', 'Keep security and essential tools enabled. Test the next sign-in before judging whether startup improved.'],
    ],
  };
  if (/task manager/.test(key)) return {
    name: 'Windows Task Manager',
    steps: [
      ['Find the busy resource', 'Use Processes or Performance to review processor, memory and disk activity.'],
      ['Match the workload', 'Compare the app and resource with the warning. High activity alone does not establish a fault.'],
      ['Save and close normally', 'Save your work and close apps you recognize using their own controls. Return to RigMD and take new readings.'],
    ],
  };
  if (/backup/.test(key)) return {
    name: 'Windows Backup settings',
    steps: [
      ['Choose what matters', 'Review the files and folders you need to protect.'],
      ['Check the destination', 'Confirm where the backup goes and whether it has completed. Opening settings does not create a backup.'],
      ['Verify before removing', 'Make sure important files can be recovered before deleting originals or continuing drive repairs.'],
    ],
  };
  if (/power/.test(key)) return {
    name: 'Windows Power settings',
    steps: [
      ['Review the setting', 'Check the power mode and sleep options for battery or plugged-in use.'],
      ['Understand the trade-off', 'Higher performance can use more power and produce more heat. A setting does not measure battery health.'],
      ['Try one change', 'Choose the mode that fits your use, then return to RigMD and compare new readings under similar conditions.'],
    ],
  };
  return { name: 'the related Windows tool', steps: [
    ['Match the finding', 'Keep the affected app or device and the reported problem in mind.'],
    ['Read the details', 'Review the actual status and available options in Windows before changing anything.'],
    ['Return and check', 'If you choose a change, review its confirmation first. Return to RigMD and check the result again.'],
  ] };
}
