import {
  encodeTextEdit,
  validateTextInput,
  type TextEdit,
  type TextInputOptions,
} from '@capsule-sdk/react/text-edit';
import type { UiNode } from './packet';

export function bindTextInput(
  field: HTMLInputElement,
  getNode: () => UiNode | undefined,
  send: (payload: Uint8Array) => void,
  signal: AbortSignal,
) {
  let options: TextInputOptions = {
    maxLength: 512,
    maxBytes: 512,
    readOnly: false,
    numeric: false,
    revision: 0,
  };
  let commanded: string | undefined,
    revision = -1,
    accepted = '',
    anchor = 0,
    focus = 0;
  let composing = false,
    composition = '',
    suppressSubmit = false;
  const live = () => !!getNode()?.visible && field.isConnected;
  const emit = (phase: TextEdit['phase'], userInput = true) => {
    if (!live()) return;
    send(
      encodeTextEdit({
        phase,
        text: accepted,
        composition,
        anchor,
        focus,
        composing,
        userInput,
      }),
    );
  };
  const selection = () => {
    const start = field.selectionStart ?? 0,
      end = field.selectionEnd ?? start;
    anchor = field.selectionDirection === 'backward' ? end : start;
    focus = field.selectionDirection === 'backward' ? start : end;
  };
  const change = (userInput = true) => {
    if (!live() || composing) return;
    try {
      if (options.readOnly && userInput && field.value !== accepted)
        throw Error('Read only');
      validateTextInput(field.value, options);
    } catch {
      field.value = accepted;
      field.setSelectionRange(
        Math.min(anchor, focus),
        Math.max(anchor, focus),
        anchor > focus ? 'backward' : 'forward',
      );
      return;
    }
    const changed = accepted !== field.value;
    accepted = field.value;
    selection();
    if (changed) emit('change', userInput);
  };
  field.type = 'text';
  field.autocomplete = 'off';
  field.spellcheck = false;
  field.addEventListener('input', () => change(), { signal });
  field.addEventListener(
    'select',
    () => {
      if (!composing) selection();
    },
    { signal },
  );
  field.addEventListener(
    'compositionstart',
    () => {
      if (options.readOnly) return;
      selection();
      composing = true;
      composition = '';
      suppressSubmit = true;
      emit('composition');
    },
    { signal },
  );
  field.addEventListener(
    'compositionupdate',
    (event) => {
      if (!composing) return;
      composition = event.data;
      // The platform keeps the full provisional string; oversized records never cross the ABI.
      try {
        emit('composition');
      } catch {
        composition = '';
      }
    },
    { signal },
  );
  field.addEventListener(
    'compositionend',
    () => {
      composing = false;
      composition = '';
      suppressSubmit = true;
      change();
      emit('composition');
    },
    { signal },
  );
  field.addEventListener(
    'keydown',
    (event) => {
      if (!live()) return;
      // Keep native editing and clipboard shortcuts local, including Escape during IME.
      if (
        event.key !== 'Tab' ||
        composing ||
        event.isComposing ||
        event.keyCode === 229
      )
        event.stopPropagation();
      if (event.key !== 'Enter' && event.key !== 'Escape') return;
      if (
        composing ||
        event.isComposing ||
        event.keyCode === 229 ||
        suppressSubmit
      )
        return;
      event.preventDefault();
      if (event.repeat) return;
      change();
      if (event.key === 'Enter') {
        if (!options.readOnly) emit('submit');
      } else {
        emit('cancel');
        field.blur();
      }
    },
    { signal },
  );
  field.addEventListener(
    'keyup',
    () => {
      suppressSubmit = false;
    },
    { signal },
  );
  field.addEventListener(
    'focus',
    () => {
      selection();
      emit('focus');
    },
    { signal },
  );
  field.addEventListener(
    'blur',
    () => {
      composing = false;
      composition = '';
      suppressSubmit = false;
      change();
      emit('blur');
    },
    { signal },
  );
  field.ownerDocument.defaultView?.addEventListener(
    'blur',
    () => {
      if (field.ownerDocument.activeElement === field) field.blur();
    },
    { signal },
  );
  return (node: UiNode) => {
    options = node.textInput ?? options;
    // Validate whole edits rather than letting the browser truncate pasted UTF-16.
    field.removeAttribute('maxlength');
    field.readOnly = options.readOnly;
    field.inputMode = options.numeric ? 'numeric' : 'text';
    if (options.readOnly && composing) field.blur();
    let invalid = false;
    try {
      validateTextInput(accepted, options);
    } catch {
      invalid = true;
    }
    if (commanded !== node.text || revision !== options.revision || invalid) {
      const initial = commanded === undefined;
      if (composing) field.blur();
      commanded = node.text;
      revision = options.revision;
      composing = false;
      composition = '';
      accepted = node.text;
      field.value = accepted;
      anchor = focus = accepted.length;
      field.setSelectionRange(focus, focus);
      if (!initial) emit('change', false);
    }
    if (!node.visible && field.ownerDocument.activeElement === field)
      field.blur();
  };
}
