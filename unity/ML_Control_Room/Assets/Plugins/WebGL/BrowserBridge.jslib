mergeInto(LibraryManager.library, {
    GetBrowserToken: function() {
        var token = window.localStorage.getItem('arena_token') || '';
        var bufferSize = lengthBytesUTF8(token) + 1;
        var buffer = _malloc(bufferSize);
        stringToUTF8(token, buffer, bufferSize);
        return buffer;
    }
});
