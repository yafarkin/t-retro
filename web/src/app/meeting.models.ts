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
  isOnline: boolean;
}

export interface MeetingCard {
  id: string;
  columnId: string;
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

export interface MeetingTopicVoter {
  userId: string;
  displayName: string;
  count: number;
}

export interface MeetingTopic {
  id: string;
  kind: 'card' | 'group';
  sourceId: string;
  totalVotes: number;
  myVotes: number;
  voters?: MeetingTopicVoter[];
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
}

export interface TopicDecision {
  id: string;
  topicId: string;
  text: string;
}

export interface ActionItem {
  id: string;
  topicId: string;
  text: string;
  assigneeId: string | null;
  exportStatus: string;
}

export interface MeetingDiscussion {
  currentTopicId: string | null;
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
  cards: MeetingCard[];
  groups: MeetingGroup[];
  topics: MeetingTopic[];
  voting?: MeetingVoting;
  discussion?: MeetingDiscussion;
  timer?: MeetingTimer;
  integrations: { ai: boolean };
  readiness?: MeetingReadiness;
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
