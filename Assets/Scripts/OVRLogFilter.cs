using UnityEngine;
using System;

public class OVRLogFilter : ILogHandler
{
    private ILogHandler m_DefaultLogHandler = Debug.unityLogger.logHandler;

    public OVRLogFilter()
    {
        Debug.unityLogger.logHandler = this;
    }

    public void LogFormat(LogType logType, UnityEngine.Object context, string format, params object[] args)
    {
        // Format the message to check its content
        string message = string.Format(format, args);

        // Filter out messages that start with [OVR
        if (!message.StartsWith("[OVR") && !message.Contains("[MetaXR"))
        {
            m_DefaultLogHandler.LogFormat(logType, context, format, args);
        }
    }

    public void LogException(Exception exception, UnityEngine.Object context)
    {
        // Always pass through exceptions
        m_DefaultLogHandler.LogException(exception, context);
    }
}
