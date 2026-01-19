mergeInto(LibraryManager.library, {
    UploadImageFile: function(gameObjectNamePtr, methodNamePtr) {
        var gameObjectName = UTF8ToString(gameObjectNamePtr);
        var methodName = UTF8ToString(methodNamePtr);

        // Create input element if it doesn't exist
        var fileInput = document.getElementById('UnityWebGLFilePicker');
        if (!fileInput) {
            fileInput = document.createElement('input');
            fileInput.setAttribute('id', 'UnityWebGLFilePicker');
            fileInput.setAttribute('type', 'file');
            fileInput.setAttribute('accept', 'image/png, image/jpeg, image/jpg');
            fileInput.style.display = 'none';
            document.body.appendChild(fileInput);
        }

        // Reset value so the same file can be selected again
        fileInput.value = null;

        fileInput.onchange = function(event) {
            var file = event.target.files[0];
            if (!file) return;

            var reader = new FileReader();
            reader.onload = function(e) {
                var arrayBuffer = e.target.result;
                var bytes = new Uint8Array(arrayBuffer);
                var binary = '';
                var len = bytes.byteLength;
                for (var i = 0; i < len; i++) {
                    binary += String.fromCharCode(bytes[i]);
                }
                var base64 = window.btoa(binary);
                
                SendMessage(gameObjectName, methodName, base64);
            };
            reader.readAsArrayBuffer(file);
        };

        fileInput.click();
    }
});