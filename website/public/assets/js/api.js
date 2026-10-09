// Same-origin API helper, pages and JSON come from the one FastAPI app,
// so requests carry the session cookie and need no base URL or auth header.
function _apiRequestForMethod(method = "GET") {
    function _inner(endpoint, params = {}, data = {}) {
        data = { ...data };
        data.method = method;
        data.headers = { "Content-Type": "application/json", ...(data.headers || {}) };

        const query = new URLSearchParams(params).toString();
        return fetch(`${endpoint}${query ? `?${query}` : ""}`, data);
    }
    return _inner;
}

const api = {
    get: _apiRequestForMethod("GET"),
    post: _apiRequestForMethod("POST"),
    put: _apiRequestForMethod("PUT"),
    delete: _apiRequestForMethod("DELETE"),
};
