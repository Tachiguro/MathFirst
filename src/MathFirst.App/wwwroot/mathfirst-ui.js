window.MathFirstUi = (() => {
    "use strict";

    // Keep this editing grammar aligned with NumericAnswerInputPolicy.IsValidEdit:
    // empty, canonical unsigned integer, or one dot/comma decimal separator.
    const canonicalUnsignedDecimal = /^(?:|0|[1-9]\d*|(?:0|[1-9]\d*)?[.,]\d*)$/;

    function isValidEdit(value, maximumLength) {
        return value.length <= maximumLength && canonicalUnsignedDecimal.test(value);
    }

    function selection(input) {
        const start = input.selectionStart ?? input.value.length;
        const end = input.selectionEnd ?? start;
        return { start, end };
    }

    function replaceSelection(input, insertedText) {
        const { start, end } = selection(input);
        return input.value.slice(0, start) + insertedText + input.value.slice(end);
    }

    function deleteProspectively(input, backwards) {
        const { start, end } = selection(input);
        if (start !== end) {
            return input.value.slice(0, start) + input.value.slice(end);
        }

        if (backwards && start > 0) {
            return input.value.slice(0, start - 1) + input.value.slice(end);
        }

        if (!backwards && end < input.value.length) {
            return input.value.slice(0, start) + input.value.slice(end + 1);
        }

        return input.value;
    }

    function beforeInputProspectiveValue(input, event) {
        if (event.inputType === "deleteContentBackward") {
            return deleteProspectively(input, true);
        }

        if (event.inputType === "deleteContentForward") {
            return deleteProspectively(input, false);
        }

        if (event.inputType.startsWith("delete")) {
            const { start, end } = selection(input);
            return start === end ? null : input.value.slice(0, start) + input.value.slice(end);
        }

        if (event.inputType.startsWith("insert") && event.data !== null) {
            return replaceSelection(input, event.data);
        }

        return null;
    }

    function attachNumericInputGuard(input, maximumLength) {
        if (!input || input.dataset.mathFirstNumericGuard === "attached") {
            return;
        }

        input.dataset.mathFirstNumericGuard = "attached";

        input.addEventListener("beforeinput", event => {
            if (event.isComposing) {
                return;
            }

            const prospectiveValue = beforeInputProspectiveValue(input, event);
            if (prospectiveValue !== null && !isValidEdit(prospectiveValue, maximumLength)) {
                event.preventDefault();
            }
        });

        // Fallback for WebViews that do not dispatch beforeinput for every keyboard path.
        input.addEventListener("keydown", event => {
            if (event.isComposing || event.ctrlKey || event.altKey || event.metaKey) {
                return;
            }

            let prospectiveValue = null;
            if (event.key === "Backspace") {
                prospectiveValue = deleteProspectively(input, true);
            } else if (event.key === "Delete") {
                prospectiveValue = deleteProspectively(input, false);
            } else if (event.key.length === 1) {
                prospectiveValue = replaceSelection(input, event.key);
            }

            if (prospectiveValue !== null && !isValidEdit(prospectiveValue, maximumLength)) {
                event.preventDefault();
            }
        });

        // ClipboardEvent exposes pasted text synchronously even when beforeinput.data is null.
        input.addEventListener("paste", event => {
            const pastedText = event.clipboardData?.getData("text") ?? "";
            if (!isValidEdit(replaceSelection(input, pastedText), maximumLength)) {
                event.preventDefault();
            }
        });
    }

    function focusAndScrollIntoView(element) {
        if (!element) {
            return;
        }

        element.focus({ preventScroll: true });
        const reduceMotion = window.matchMedia("(prefers-reduced-motion: reduce)").matches;
        element.scrollIntoView({
            behavior: reduceMotion ? "auto" : "smooth",
            block: "nearest",
            inline: "nearest"
        });
    }

    return {
        attachNumericInputGuard,
        focusAndScrollIntoView
    };
})();
