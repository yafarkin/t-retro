import { expect, Page, test } from '@playwright/test';
import { advanceBeforeEveryoneIsReady, closeContextGracefully, voteControls } from './helpers';

async function login(page: Page, baseURL: string, subject: string, displayName: string): Promise<void> {
  await page.goto(`${baseURL}/login`);
  await page.getByLabel('Идентификатор сотрудника').fill(subject);
  await page.getByLabel('Отображаемое имя').fill(displayName);
  await page.getByRole('button', { name: 'Войти', exact: true }).click();
  await expect(page.getByRole('heading', { name: 'Мои встречи' })).toBeVisible();
}

async function addCard(page: Page, text: string): Promise<void> {
  const column = page.getByRole('region', { name: 'Начать', exact: true });
  await column.getByRole('button', { name: 'Добавить карточку' }).click();
  await column.getByLabel('Новая карточка').fill(text);
  await column.getByRole('button', { name: 'Добавить карточку' }).click();
  await expect(column.getByText(text, { exact: true })).toBeVisible();
}

test('M5 topics, live votes, late budget, open breakdown and exclusion work in independent contexts', async ({ browser, baseURL }) => {
  const origin = baseURL!;
  const firstContext = await browser.newContext();
  const secondContext = await browser.newContext();
  const lateContext = await browser.newContext();
  const first = await firstContext.newPage();
  const second = await secondContext.newPage();
  const late = await lateContext.newPage();

  await login(first, origin, 'm5-first', 'Первый M5');
  await login(second, origin, 'm5-second', 'Второй M5');
  await login(late, origin, 'm5-late', 'Поздний M5');
  await first.goto(`${origin}/meetings/new`);
  await first.getByRole('button', { name: 'Выбрать шаблон' }).click();
  await first.getByLabel('Название встречи').fill('Ретро M5 голосование');
  await first.getByRole('button', { name: 'Настройки ретро' }).click();
  await first.getByLabel('Скрывать индивидуальные голоса').uncheck();
  await first.getByRole('button', { name: 'Голосование', exact: true }).click();
  await first.getByLabel('Распределение голосов').selectOption('percent');
  await first.getByRole('button', { name: 'Сохранить настройки' }).click();
  await first.getByRole('button', { name: 'Создать ретро', exact: true }).click();
  await first.getByRole('button', { name: 'Пригласить', exact: true }).click();
  const invite = await first.getByLabel('Ссылка приглашения').inputValue();
  await first.getByRole('button', { name: 'Закрыть приглашение' }).click();
  await second.goto(invite);
  await second.getByRole('button', { name: 'Вступить во встречу' }).click();
  await expect(second.locator('.phase-roadmap [aria-current="step"]')).toHaveText('Сбор карточек');

  await addCard(first, 'Первая M5');
  await addCard(second, 'Вторая M5');
  await addCard(first, 'Третья M5');
  await advanceBeforeEveryoneIsReady(first, 'Перейти к группировке');
  await expect(second.locator('.phase-roadmap [aria-current="step"]')).toHaveText('Группировка');
  const secondCard = second.locator('.card').filter({ has: second.getByText('Вторая M5', { exact: true }) });
  await secondCard.dragTo(second.locator('.card').filter({ has: second.getByText('Первая M5', { exact: true }) }));
  const group = first.locator('.card-group').filter({ hasText: 'Первая M5' });
  await group.getByRole('button', { name: 'Развернуть группу' }).click();
  await expect(group.locator('.card')).toHaveCount(2);

  await advanceBeforeEveryoneIsReady(first, 'Открыть голосование');
  await expect(second.locator('.phase-roadmap [aria-current="step"]')).toHaveText('Голосование');
  await expect(second.locator('.board app-meeting-topic-votes')).toHaveCount(2);
  await expect(second.getByRole('heading', { name: 'Темы для голосования' })).toHaveCount(0);
  await expect(second.getByRole('status', { name: 'Мои оставшиеся голоса' })).toHaveText('2');
  await expect(second.getByRole('button', { name: 'Настройки голосования' })).toHaveCount(0);
  await expect(first.getByRole('button', { name: 'Настройки голосования' })).toHaveText('2');
  await expect(group.locator('.group-cards app-meeting-topic-votes')).toHaveCount(0);

  const firstGroupTopic = voteControls(first, 'Первая M5');
  await firstGroupTopic.getByRole('button', { name: 'Проголосовать: Первая M5' }).click();
  await expect(firstGroupTopic.getByLabel('Ваши голоса')).toHaveText('Мои 1');
  const secondGroupTopic = voteControls(second, 'Первая M5');
  await expect(secondGroupTopic.getByLabel('Всего голосов')).toHaveText('1');
  await secondGroupTopic.getByRole('button', { name: 'Расклад голосов' }).click();
  await expect(secondGroupTopic.getByRole('list', { name: 'Расклад голосов' })).toContainText(/Первый M5\s*1/);
  await secondGroupTopic.getByRole('button', { name: 'Проголосовать: Первая M5' }).click();
  await expect(secondGroupTopic.getByLabel('Ваши голоса')).toHaveText('Мои 1');
  await secondGroupTopic.getByRole('button', { name: 'Проголосовать: Первая M5' }).click();
  await expect(secondGroupTopic.getByLabel('Ваши голоса')).toHaveText('Мои 2');
  await expect(second.getByRole('status', { name: 'Мои оставшиеся голоса' })).toHaveText('0');
  await expect(secondGroupTopic.getByLabel('Всего голосов')).toHaveText('3');
  await expect(secondGroupTopic.getByRole('button', { name: 'Проголосовать: Первая M5' })).toBeDisabled();

  await late.goto(invite);
  await late.getByRole('button', { name: 'Вступить во встречу' }).click();
  await expect(late.getByRole('status', { name: 'Мои оставшиеся голоса' })).toHaveText('2');
  const lateSingleTopic = voteControls(late, 'Третья M5');
  await lateSingleTopic.getByRole('button', { name: 'Проголосовать: Третья M5' }).click();
  await expect(lateSingleTopic.getByLabel('Ваши голоса')).toHaveText('Мои 1');
  const firstSingleTopic = voteControls(first, 'Третья M5');
  await expect(firstSingleTopic.getByLabel('Всего голосов')).toHaveText('1');

  await firstGroupTopic.getByRole('button', { name: 'Отозвать голос: Первая M5' }).click();
  await expect(firstGroupTopic.getByLabel('Ваши голоса')).toHaveText('Мои 0');
  await expect(secondGroupTopic.getByLabel('Всего голосов')).toHaveText('2');
  await first.getByRole('button', { name: 'Настройки голосования' }).click();
  const budget = first.getByRole('dialog', { name: 'Настройки голосования' });
  await expect(budget.getByLabel('Голосов на человека')).toHaveValue('2');
  await budget.getByLabel('Голосов на человека').fill('1');
  await budget.getByRole('button', { name: 'Сохранить бюджет' }).click();
  await expect(budget.getByRole('alert')).toBeVisible();
  await budget.getByLabel('Голосов на человека').fill('3');
  await budget.getByRole('button', { name: 'Сохранить бюджет' }).click();
  await expect(budget).not.toBeVisible();
  await expect(first.getByRole('button', { name: 'Настройки голосования' })).toHaveText('3');
  await expect(second.getByRole('status', { name: 'Мои оставшиеся голоса' })).toHaveText('1');
  await expect(late.getByRole('status', { name: 'Мои оставшиеся голоса' })).toHaveText('2');

  await first.getByRole('button', { name: 'Участники', exact: true }).click();
  await first.getByRole('radio', { name: /Второй M5/ }).check();
  first.once('dialog', (dialog) => dialog.accept());
  await first.getByRole('button', { name: 'Исключить', exact: true }).click();
  await expect(second.getByRole('heading', { name: 'Доступ к встрече закрыт' })).toBeVisible();
  await first.getByRole('button', { name: 'Закрыть список участников' }).click();
  await expect(firstGroupTopic.getByLabel('Всего голосов')).toHaveText('2');
  await firstGroupTopic.getByRole('button', { name: 'Расклад голосов' }).click();
  await expect(firstGroupTopic.getByRole('list', { name: 'Расклад голосов' })).toContainText(/Второй M5\s*2/);

  await Promise.all([firstContext, secondContext, lateContext].map(closeContextGracefully));
});
