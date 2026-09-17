// Alpine.js bootstrap for the Portal. Registers global state Alpine needs before it starts
// (Alpine.start() runs automatically when alpine.min.js loads, right after 'alpine:init' fires).
//
// This app has one global need Alpine's local component scoping can't express on its own:
// a modal can be triggered from a link that isn't a DOM sibling of the modal markup itself
// (e.g. a link inside a table row opening a modal rendered later, after the whole table).
// Bootstrap solved this with id-based data-bs-target="#id" targeting; the $store.modal below
// is the same idea — open/close a modal by id from anywhere on the page.
document.addEventListener('alpine:init', () => {
    Alpine.store('modal', {
        activeId: null,
        open(id) { this.activeId = id; },
        close() { this.activeId = null; }
    });
});

// Closes any open modal on Escape, matching Bootstrap's modal keyboard behavior.
document.addEventListener('keydown', (e) => {
    if (e.key === 'Escape' && window.Alpine?.store('modal')?.activeId) {
        window.Alpine.store('modal').close();
    }
});
