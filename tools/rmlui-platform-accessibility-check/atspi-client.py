#!/usr/bin/env python3
"""Actual external pyatspi client. No fixture accessibility nodes or input logs."""
import time
import pyatspi

deadline = time.monotonic() + 20

def application():
    desktop = pyatspi.Registry.getDesktop(0)
    for i in range(desktop.childCount):
        app = desktop.getChildAtIndex(i)
        if app.name == 'Project Prime':
            return app
    raise RuntimeError('native AT-SPI application not registered')

def find(name):
    last = None
    while time.monotonic() < deadline:
        try:
            app = application()
            for i in range(app.childCount):
                node = app.getChildAtIndex(i)
                if node.name == name:
                    return node
        except Exception as error:
            last = type(error).__name__
        time.sleep(.03)
    raise RuntimeError('AT-SPI real control unavailable: '+name+' ('+str(last)+')')

email = find('Email address')
assert email.getRole() == pyatspi.ROLE_ENTRY
password = find('Password')
assert password.getRole() == pyatspi.ROLE_PASSWORD_TEXT
assert 'Text' not in email.getInterfaces()
assert 'Text' not in password.getInterfaces()
assert email.queryComponent().grabFocus()
time.sleep(.1)
email = find('Email address')
assert email.queryEditableText().setTextContents('日本語 😀')
time.sleep(.1)
email = find('Email address')
bounds = email.queryComponent().getExtents(pyatspi.DESKTOP_COORDS)
assert bounds.width > 0 and bounds.height > 0
save = find('SAVE SETTINGS')
action = save.queryAction()
click = next(i for i in range(action.nActions) if action.getName(i) == 'click')
assert action.doAction(click)
print('PASS actual AT-SPI application/name/role/password/no-private-text/focus/Unicode/edit/geometry/invoke')
