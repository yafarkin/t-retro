export type MeetingPhase = 'Draft' | 'Collect' | 'Group' | 'Vote' | 'Discuss' | 'Closed';
export type VoteMode = 'percent' | 'fixed';
export type TemplateVisibility = 'personal' | 'shared';

export interface EditableColumn {
  name: string;
  color: string;
}

export interface Column extends EditableColumn {
  id: string;
  position: number;
}

export interface VoteSettings {
  mode: VoteMode;
  percent: number;
  minimum: number;
  fixed: number | null;
}

export interface MeetingSettingsPayload {
  name: string;
  columns: EditableColumn[];
  anonymousCards: boolean;
  privateCollection: boolean;
  anonymousVotes: boolean;
  voteSettings: VoteSettings;
}

export interface MeetingSummary {
  id: string;
  name: string;
  phase: MeetingPhase;
  createdAtUtc: string;
}

export interface Participant {
  id: string;
  subject: string;
  displayName: string;
  status: 'Active' | 'Excluded';
}

export interface MeetingState extends MeetingSettingsPayload {
  serverNowUtc: string;
  id: string;
  phase: MeetingPhase;
  version: number;
  boardVersion: number;
  canManage: boolean;
  facilitatorId: string | null;
  columns: Column[];
  participants: Participant[];
}

export interface Invite {
  url: string;
  version: number;
}

export interface MeetingTemplate {
  id: string;
  ownerId: string;
  name: string;
  visibility: TemplateVisibility;
  columns: Column[];
}

export function defaultColumns(): EditableColumn[] {
  return [
    { name: 'Начать', color: '#36A269' },
    { name: 'Перестать', color: '#D34E4E' },
    { name: 'Продолжать', color: '#4C86D9' },
  ];
}

export function defaultVoteSettings(): VoteSettings {
  return { mode: 'percent', percent: 5, minimum: 2, fixed: null };
}
