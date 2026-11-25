export type MeetingPhase = 'Draft' | 'Collect' | 'Group' | 'Vote' | 'Discuss' | 'Closed';
export type VoteMode = 'percent' | 'fixed';
export type TemplateVisibility = 'personal' | 'shared';

export interface EditableColumn {
  name: string;
  color: string;
  description?: string | null;
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
  votingEnabled?: boolean;
  voteSettings: VoteSettings;
}

export interface CreateMeetingPayload extends MeetingSettingsPayload {
  startCollecting?: boolean;
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
  isOnline: boolean;
  isReady: boolean;
}

export interface MeetingCard {
  id: string;
  columnId: string;
  originalColumnColor?: string;
  text?: string;
  authorId?: string;
  authorDisplayName?: string;
  isMine: boolean;
  isHidden: boolean;
  version: number;
  groupId: string | null;
}

export interface MeetingGroup {
  id: string;
  columnId: string;
  name: string;
}

export interface MeetingReadiness {
  readyCount: number;
  totalCount: number;
  myReady: boolean;
}

export interface MeetingTopic {
  id: string;
  kind: 'card' | 'group';
  sourceId: string;
  totalVotes?: number;
  myVotes?: number;
}

export interface MeetingVoting {
  originalCardCount: number;
  budget: number;
  spent: number;
  remaining: number;
  ballotVersion: number;
}

export type DiscussionStatus = 'pending' | 'discussed' | 'skipped';

export interface DiscussionTopic {
  topicId: string;
  position: number;
  status: DiscussionStatus;
}

export interface TopicComment {
  id: string;
  topicId: string;
  text: string;
  createdVersion: number | null;
}

export interface TopicDecision {
  id: string;
  topicId: string;
  text: string;
  createdVersion: number | null;
}

export interface ActionItem {
  id: string;
  topicId: string;
  text: string;
  createdVersion: number | null;
  description: string | null;
  assigneeId: string | null;
  exportStatus: 'notExported' | 'pending' | 'created' | 'rejected' | 'unknown' | 'unavailable';
  externalRequestId: string | null;
  issueKey: string | null;
  issueUrl: string | null;
}

export interface JiraExportResult {
  status: string;
  externalRequestId: string;
  issueKey: string | null;
  issueUrl: string | null;
  version: number;
}

export interface MeetingDiscussion {
  currentTopicId: string | null;
  selectionVersion: number;
  queue: DiscussionTopic[];
  comments: TopicComment[];
  decisions: TopicDecision[];
  actions: ActionItem[];
}

export interface SuggestedGroup {
  name: string;
  columnId: string;
  cardIds: string[];
}

export interface GroupProposal {
  id: string;
  boardVersion: number;
  groups: SuggestedGroup[];
}

export interface MeetingTimer {
  runId: string;
  phase: MeetingPhase;
  endsAtUtc: string;
  serverNowUtc: string;
}

export interface MeetingTotals {
  closedAtUtc: string;
  participantCount: number;
  cardCount: number;
  topicCount: number;
  voteCount: number;
}

export interface MeetingState extends MeetingSettingsPayload {
  isArchived: boolean;
  serverNowUtc: string;
  id: string;
  phase: MeetingPhase;
  version: number;
  boardVersion: number;
  canManage: boolean;
  facilitatorId: string | null;
  columns: Column[];
  participants: Participant[];
  cards: MeetingCard[];
  groups: MeetingGroup[];
  topics: MeetingTopic[];
  voting?: MeetingVoting;
  discussion?: MeetingDiscussion;
  timer?: MeetingTimer;
  integrations: { ai: boolean; jira: boolean };
  readiness?: MeetingReadiness;
  totals?: MeetingTotals;
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
    { name: 'Начать', color: '#36A269', description: 'Практики, которые стоит попробовать' },
    { name: 'Перестать', color: '#D34E4E', description: 'Практики, от которых стоит отказаться' },
    { name: 'Продолжать', color: '#4C86D9', description: 'Практики, которые стоит сохранить' },
  ];
}

export function copyEditableColumns(columns: readonly EditableColumn[]): EditableColumn[] {
  return columns.map(({ name, color, description }) => ({ name, color, description }));
}

export function defaultVoteSettings(): VoteSettings {
  return { mode: 'percent', percent: 5, minimum: 2, fixed: null };
}
