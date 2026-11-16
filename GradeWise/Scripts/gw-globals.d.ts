// Type definitions for the GradeWise browser scripts (editor only).
// No page loads this file. Visual Studio / TypeScript read it through the
// "/// <reference path="gw-globals.d.ts" />" line at the top of each script, so the shared
// window.GW helpers (Scripts/gw-modal.js) and the Academic terms helpers have real types.

// ---------- window.GW (Scripts/gw-modal.js) ----------

/** One piece of a dialog paragraph: plain, bold or muted text. */
interface GWDialogPiece {
    text: string;
    bold?: boolean;
    muted?: boolean;
}

/** Options for GW.confirm and GW.alert. */
interface GWDialogOptions {
    title?: string;
    /** One paragraph of plain text. */
    message?: string;
    /** Several paragraphs instead of "message": one inner array per paragraph. */
    paragraphs?: GWDialogPiece[][];
    okText?: string;
    cancelText?: string;
    /** "danger" turns the OK button red and focuses Cancel first. */
    tone?: string;
}

/** Options for GW.modal.open. */
interface GWModalOpenOptions {
    /** The element to focus when the modal opens. */
    focus?: HTMLElement | null;
    /** Called after the modal closes (Esc, backdrop click, a close button or GW.modal.close). */
    onClose?: (() => void) | null;
}

interface GWModalApi {
    /** Opens a markup-defined modal (an element with class gw-modal-backdrop), by element or id. */
    open(target: string | HTMLElement | null, options?: GWModalOpenOptions): HTMLElement | null;
    /** Closes a modal and returns focus to whatever opened it. */
    close(target: string | HTMLElement | null): void;
}

/** "success" | "warning" | "error"; anything else is shown as "success". */
type GWToastKind = string;

interface GWToastOptions {
    title?: string;
    /** Extra lines shown as a short list under the message. */
    lines?: string[];
    /** How long the toast stays, in milliseconds. */
    duration?: number;
}

interface GWToastHandle {
    close(): void;
}

/** A server message (data-gw-flash banner) that was shown as a toast. */
interface GWFlash {
    kind: string;
    text: string;
}

interface GWApi {
    /** Resolves true when the user chooses OK, false for Cancel / Esc / backdrop click. */
    confirm(options?: GWDialogOptions): Promise<boolean>;
    /** Resolves when the user closes the notice. */
    alert(options?: GWDialogOptions): Promise<void>;
    modal: GWModalApi;
    toast(kind: GWToastKind, message: string, options?: GWToastOptions): GWToastHandle;
    /** Shows the page's data-gw-flash banners as toasts (once) and returns them. */
    flashes(): GWFlash[];
}

// ---------- Academic terms page (Scripts/admin-terms.js) ----------

/** A term as the server sends it in data-terms (what the database stores). */
interface GWTermRecord {
    id: string;
    name: string;
    isCurrent?: boolean;
    /** Saved dates, yyyy-mm-dd; "" or missing for terms saved without dates. */
    startDate?: string;
    endDate?: string;
    /** Why the server would refuse to delete the term ("" or missing = it can be deleted). */
    deleteBlocked?: string;
}

/** A term as the Academic terms page works with it. */
interface GWTerm {
    id: string;
    name: string;
    /** "1st Semester" | "2nd Semester" | "Summer", or "" when the name has an old format. */
    semester: string;
    /** "2026-2027", or "" when the name has an old format. */
    academicYear: string;
    /** yyyy-mm-dd from the database, or "" for terms saved without dates. */
    startDate: string;
    endDate: string;
    isCurrent: boolean;
    archived: boolean;
    archivedOn: string;
    /** Why the term cannot be deleted (in use, or courses use it); "" when it can. From the server. */
    deleteBlocked: string;
}

/** The terms on the page, loaded from the database. Archive / restore change only this copy (not saved). */
interface GWTermStoreApi {
    parseName(name: string | null | undefined): { semester: string; academicYear: string };
    buildName(semester: string, academicYear: string): string;
    load(initial: GWTermRecord[] | null | undefined): void;
    list(): GWTerm[];
    get(id: string | null): GWTerm | null;
    hasChanges(): boolean;
    archive(id: string, today: string): Promise<GWTerm>;
    restore(id: string): Promise<GWTerm>;
}

/** The server actions the Academic terms page calls (saved to the database). */
interface GWTermServerApi {
    /** Posts to Admin/SetCurrentTerm/{id}; the page reloads. */
    setCurrent(id: string): void;
    /** Posts to Admin/DeleteTerm/{id}; the page reloads with the server's result. */
    deleteTerm(id: string): void;
    /** The Admin/EditTerm/{id} address the edit form posts to (null if the page does not provide it). */
    editUrl(id: string): string | null;
}

interface Window {
    GW?: GWApi;
    GradeWiseTermStore?: GWTermStoreApi;
    GradeWiseTermServer?: GWTermServerApi;
}
