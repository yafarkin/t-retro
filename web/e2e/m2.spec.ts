import { expect, Page, test } from '@playwright/test';
import { closeContextGracefully } from './helpers';

async function login(page: Page, baseURL: string, subject: string, displayName: string): Promise<void> {
  await page.goto(`${baseURL}/login`);
  await page.getByLabel('Идентификатор сотрудника').fill(subject);
  await page.getByLabel('Отображаемое имя').fill(displayName);
  await page.getByRole('button', { name: 'Войти', exact: true }).click();
  await expect(page.getByRole('heading', { name: 'Мои встречи' })).toBeVisible();
}

test('M2 presence, live state, role transfer and access revocation synchronize clients', async ({ browser, baseURL }) => {
  const origin = baseURL!;
  const firstContext = await browser.newContext();
  const secondContext = await browser.newContext();
  const first = await firstContext.newPage();
  const second = await secondContext.newPage();

  await login(first, origin, 'm2-first', 'Первый M2');
  await login(second, origin, 'm2-second', 'Второй M2');
  const draft = await firstContext.request.post(`${origin}/api/meetings`, {
    data: {
      name: 'Ретро M2 realtime',
      columns: [{ name: 'Начать', color: '#36A269' }],
    },
  });
  expect(draft.ok()).toBeTruthy();
  const { id } = await draft.json();
  await first.goto(`${origin}/meetings/${id}`);
  await expect(first.getByRole('heading', { name: 'Ретро M2 realtime', exact: true })).toBeVisible();
  await first.getByRole('button', { name: 'Пригласить', exact: true }).click();
  const invite = await first.getByLabel('Ссылка приглашения').inputValue();
  await first.getByRole('button', { name: 'Закрыть приглашение' }).click();

  await second.goto(invite);
  await second.getByRole('button', { name: 'Вступить во встречу' }).click();
  await expect(second.getByRole('heading', { name: 'Ретро M2 realtime', exact: true })).toBeVisible();
  await expect(second.getByRole('banner').getByRole('heading', { name: 'Ретро M2 realtime' })).toBeVisible();
  await expect(second.getByRole('main').getByRole('link', { name: /Мои встречи/ })).toHaveCount(0);
  await expect(second.getByText('Фасилитатор Первый M2.', { exact: true })).toBeVisible();
  await expect(second.getByRole('button', { name: 'Начать сбор' })).toHaveCount(0);
  await expect(first.locator('.participant-avatar').filter({ hasText: 'Второй M2' })).toContainText('онлайн');
  await second.getByRole('button', { name: 'Участники', exact: true }).click();
  const readOnlyParticipants = second.getByRole('dialog', { name: 'Участники', exact: true });
  await expect(readOnlyParticipants.getByRole('listitem')).toHaveCount(2);
  await expect(readOnlyParticipants.getByRole('radio')).toHaveCount(0);
  await expect(readOnlyParticipants.getByRole('button', { name: 'Пригласить участника' })).toHaveCount(0);
  await readOnlyParticipants.getByRole('button', { name: 'Закрыть список участников' }).click();

  const secondTab = await firstContext.newPage();
  await secondTab.goto(first.url());
  await expect(secondTab.getByRole('heading', { name: 'Ретро M2 realtime', exact: true })).toBeVisible();
  await secondTab.close();
  await expect(first.locator('.participant-avatar').filter({ hasText: 'Первый M2' })).toContainText('онлайн');

  await first.getByLabel('Название встречи').fill('Ретро M2 после обновления');
  await first.getByRole('button', { name: 'Сохранить настройки' }).click();
  await expect(second.getByRole('heading', { name: 'Ретро M2 после обновления', exact: true })).toBeVisible();

  await expect(first.getByRole('button', { name: 'Передать роль', exact: true })).toHaveCount(0);
  await first.getByRole('button', { name: 'Участники', exact: true }).click();
  const participants = first.getByRole('dialog', { name: 'Участники', exact: true });
  await participants.getByRole('button', { name: 'Пригласить участника' }).click();
  const nestedInvite = first.getByRole('dialog', { name: 'Пригласить команду' });
  const nestedUrl = new URL(await nestedInvite.getByLabel('Ссылка приглашения').inputValue());
  expect(nestedUrl.origin + nestedUrl.pathname).toBe(`${origin}/join`);
  expect(nestedUrl.searchParams.has('token')).toBe(true);
  await nestedInvite.getByRole('button', { name: 'Закрыть приглашение' }).click();
  await expect(participants.getByRole('radio')).toHaveCount(1);
  await expect(participants.getByRole('button', { name: 'Передать роль' })).toBeDisabled();
  await participants.getByRole('radio', { name: /Второй M2/ }).check();
  await participants.getByRole('button', { name: 'Передать роль' }).click();
  await expect(second.getByRole('button', { name: 'Пригласить', exact: true })).toBeVisible();
  await expect(first.locator('.participant-avatar.facilitator')).toContainText('Второй M2');
  await expect(first.getByText('Фасилитатор Второй M2.', { exact: true })).toBeVisible();
  await expect(second.getByRole('button', { name: 'Начать сбор' })).toBeVisible();
  await expect(first.getByText('Настройки изменяет только фасилитатор.')).toBeVisible();
  await expect(first.getByText('Роль фасилитатора передана.', { exact: true })).toHaveCount(0);

  await second.getByRole('button', { name: 'Участники', exact: true }).click();
  const management = second.getByRole('dialog', { name: 'Участники', exact: true });
  await management.getByRole('radio', { name: /Первый M2/ }).check();
  second.once('dialog', (dialog) => dialog.accept());
  await management.getByRole('button', { name: 'Исключить', exact: true }).click();
  await expect(first.getByRole('heading', { name: 'Доступ к встрече закрыт' })).toBeVisible();
  await expect(first.getByRole('banner').getByRole('heading')).toHaveCount(0);
  await expect(management.getByRole('listitem').filter({ hasText: 'Первый M2' })).toContainText('Исключён');
  await expect(management.getByRole('radio')).toHaveCount(0);
  await management.getByRole('button', { name: 'Закрыть список участников' }).click();
  await expect(second.locator('.participant-avatar').filter({ hasText: 'Первый M2' })).toContainText('офлайн');
  await second.getByRole('link', { name: 'T-Retro, главная' }).click();
  await expect(second.getByRole('heading', { name: 'Мои встречи' })).toBeVisible();
  await expect(second.getByRole('banner').getByRole('heading')).toHaveCount(0);

  await Promise.all([firstContext, secondContext].map(closeContextGracefully));
});
