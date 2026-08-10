function forceStopMatch(id) {
    api.post("/admin/match/stop", { id })
        .then(r => {
            if (r.status === 200) document.location.reload();
            else alert("Failed.");
        })
        .catch(e => console.error("Error:", e));
}
