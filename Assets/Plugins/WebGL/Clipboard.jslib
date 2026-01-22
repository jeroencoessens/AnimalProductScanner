mergeInto(LibraryManager.library, {
    WebGLGetClipboardText: function(gameObjectNamePtr, methodNamePtr) {
        var gameObjectName = UTF8ToString(gameObjectNamePtr);
        var methodName = UTF8ToString(methodNamePtr);

        if (navigator.clipboard && navigator.clipboard.readText) {
            navigator.clipboard.readText().then(function(text) {
                SendMessage(gameObjectName, methodName, text);
            }).catch(function(err) {
                console.error('Failed to read clipboard contents: ', err);
                SendMessage(gameObjectName, methodName, "");
            });
        } else {
            console.warn('Clipboard API not available or not allowed.');
            SendMessage(gameObjectName, methodName, "");
        }
    }
});