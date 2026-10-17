// Experimental React bailout prefix; build --frontend bailout appends the backend.
import './scheduler';
import React from 'react';
import Reconciler from 'react-reconciler';
import { anchorValues, effectValues } from '@capsule/react/presentation';
import { decodeTextEdit, validateTextInput } from '@capsule/react/text-edit';
import { pump, clearTasks } from './scheduler';
const native = (globalThis as any).portableUi;
type Node = { type: string; props: any; children: Node[]; handle: bigint };
const root = { children: [] as Node[] };
const append = (parent: any, child: Node) => {
  parent.children = parent.children.filter((n: Node) => n !== child);
  parent.children.push(child);
  dirty = true;
};
const remove = (parent: any, child: Node) => {
  parent.children = parent.children.filter((n: Node) => n !== child);
  dirty = true;
};
const insert = (parent: any, child: Node, before: Node) => {
  remove(parent, child);
  parent.children.splice(parent.children.indexOf(before), 0, child);
  dirty = true;
};
let dirty = false;
const hostContext = Object.freeze({});
function changedProps(type: string, before: any, after: any) {
  for (const key in before) {
    if (key === 'children' && type !== 'text') continue;
    if (!Object.is(before[key], after[key])) return true;
  }
  for (const key in after) {
    if (key === 'children' && type !== 'text') continue;
    if (!Object.prototype.hasOwnProperty.call(before, key)) return true;
  }
  return false;
}
const renderer = Reconciler({
  supportsMutation: true,
  supportsPersistence: false,
  supportsHydration: false,
  isPrimaryRenderer: true,
  getRootHostContext: () => hostContext,
  getChildHostContext: () => hostContext,
  getPublicInstance: (n: Node) => n,
  prepareForCommit: () => null,
  resetAfterCommit: () => {},
  createInstance: (type: string, props: any) => ({
    type,
    props,
    children: [],
    handle: native.reserve(),
  }),
  createTextInstance: () => {
    throw Error('Text must be inside Text');
  },
  appendInitialChild: append,
  appendChild: append,
  appendChildToContainer: append,
  insertBefore: insert,
  insertInContainerBefore: insert,
  removeChild: remove,
  removeChildFromContainer: remove,
  finalizeInitialChildren: () => false,
  prepareUpdate: (_node: Node, type: string, before: any, after: any) =>
    changedProps(type, before, after) ? true : null,
  commitUpdate: (
    node: Node,
    _payload: any,
    _type: string,
    _old: any,
    props: any,
  ) => {
    node.props = props;
    dirty = true;
  },
  shouldSetTextContent: (type: string) => type === 'text',
  resetTextContent: () => {},
  clearContainer: (container: any) => {
    container.children = [];
    dirty = true;
  },
  scheduleTimeout: setTimeout,
  cancelTimeout: clearTimeout,
  noTimeout: -1,
  getCurrentEventPriority: () => 1,
  preparePortalMount: () => {},
  detachDeletedInstance: () => {},
  hideInstance: (node: Node) => {
    node.props = { ...node.props, visible: false };
    dirty = true;
  },
  unhideInstance: (node: Node) => {
    node.props = { ...node.props, visible: true };
    dirty = true;
  },
  beforeActiveInstanceBlur: () => {},
  afterActiveInstanceBlur: () => {},
} as any);
const container = renderer.createContainer(
  root,
  0,
  null,
  false,
  null,
  '',
  (e: Error) => {
    throw e;
  },
  null,
);
const element = (type: string) => (props: any) =>
  React.createElement(type, props);
export const Box = element('box'),
  WorldAnchor = element('box'),
  Effect = element('box'),
  Text = element('text'),
  TextInput = element('textinput'),
  Image = element('image'),
  Slot = element('slot'),
  Window = element('window'),
  Progress = element('progress'),
  Portrait = element('portrait'),
  Viewport = element('viewport'),
  Overlay = element('overlay');
