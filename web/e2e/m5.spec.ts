import { expect, Page, test } from '@playwright/test';
import { closeContextGracefully } from './helpers';

async function login(page: Page, baseURL: string, subject: string, displayName: string): Promise<void> {
  await page.goto(`${baseURL}/login`);
  await page.getByLabel('Идентификатор сотрудника').fill(subject);
  await page.getByLabel('Отображаемое имя').fill(displayName);
  await page.getByRole('button', { name: 'Войти', exact: true }).click();
  await expect(page.getByRole('heading', { name: 'Мои встречи' })).toBeVisible();
}

async function addCard(page: Page, text: string): Promise<void> {
  const column = page.getByRole('region', { name: 'Начать', exact: true });
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
  await first.getByLabel('Название встречи').fill('Ретро M5 голосование');
  await first.getByLabel('Скрывать индивидуальные голоса').uncheck();
  await first.getByRole('button', { name: 'Создать встречу' }).click();
  await first.getByRole('button', { name: 'Показать приглашение' }).click();
  const invite = await first.getByLabel('Ссылка приглашения').inputValue();
  await second.goto(invite);
  await second.getByRole('button', { name: 'Вступить во встречу' }).click();
  await first.getByRole('button', { name: 'Начать сбор' }).click();
  await expect(second.getByText('Сбор карточек', { exact: true })).toBeVisible();

  await addCard(first, 'Первая M5');
  await addCard(second, 'Вторая M5');
  await addCard(first, 'Третья M5');
  await first.getByRole('button', { name: 'Перейти к группировке' }).click();
  await expect(second.getByText('Группировка', { exact: true })).toBeVisible();
  const secondCard = second.locator('.card').filter({ has: second.getByText('Вторая M5', { exact: true }) });
  await secondCard.getByLabel('Назначение переноса').selectOption({ label: 'Карточка: Первая M5' });
  await secondCard.getByRole('button', { name: 'Переместить', exact: true }).click();
  const group = first.locator('.card-group').filter({ hasText: 'Первая M5' });
  await expect(group.locator('.card')).toHaveCount(2);

  await first.getByRole('button', { name: 'Открыть голосование' }).click();
  await expect(second.getByText('Голосование', { exact: true })).toBeVisible();
  await expect(second.locator('.vote-topic')).toHaveCount(2);
  await expect(second.locator('.voting-summary')).toContainText('Исходных карточек: 3');
  await expect(second.locator('.voting-summary')).toContainText('= 2');
  await expect(second.locator('.voting-summary')).toContainText('Осталось: 2');
  await expect(group.getByRole('button', { name: /Проголосовать:/ })).toHaveCount(0);

  const firstGroupTopic = first.locator('.vote-topic').filter({ has: first.getByRole('heading', { name: 'Первая M5', exact: true }) });
  await firstGroupTopic.getByRole('button', { name: 'Проголосовать: Первая M5' }).click();
  await expect(firstGroupTopic).toContainText('Ваши голоса: 1');
  const secondGroupTopic = second.locator('.vote-topic').filter({ has: second.getByRole('heading', { name: 'Первая M5', exact: true }) });
  await expect(secondGroupTopic).toContainText('Всего голосов: 1');
  await expect(secondGroupTopic.getByLabel('Расклад голосов')).toContainText('Первый M5: 1');
  await secondGroupTopic.getByRole('button', { name: 'Проголосовать: Первая M5' }).click();
  await expect(secondGroupTopic).toContainText('Ваши голоса: 1');
  await secondGroupTopic.getByRole('button', { name: 'Проголосовать: Первая M5' }).click();
  await expect(secondGroupTopic).toContainText('Ваши голоса: 2');
  await expect(second.locator('.voting-summary')).toContainText('Осталось: 0');
  await expect(secondGroupTopic).toContainText('Всего голосов: 3');

  await late.goto(invite);
  await late.getByRole('button', { name: 'Вступить во встречу' }).click();
  await expect(late.locator('.voting-summary')).toContainText('Исходных карточек: 3');
  await expect(late.locator('.voting-summary')).toContainText('Бюджет: 2');
  await expect(late.locator('.voting-summary')).toContainText('Осталось: 2');
  const lateSingleTopic = late.locator('.vote-topic').filter({ has: late.getByRole('heading', { name: 'Третья M5', exact: true }) });
  await lateSingleTopic.getByRole('button', { name: 'Проголосовать: Третья M5' }).click();
  await expect(lateSingleTopic).toContainText('Ваши голоса: 1');
  const firstSingleTopic = first.locator('.vote-topic').filter({ has: first.getByRole('heading', { name: 'Третья M5', exact: true }) });
  await expect(firstSingleTopic).toContainText('Всего голосов: 1');

  await firstGroupTopic.getByRole('button', { name: 'Отозвать голос: Первая M5' }).click();
  await expect(firstGroupTopic).toContainText('Ваши голоса: 0');
  await expect(secondGroupTopic).toContainText('Всего голосов: 2');
  await first.getByLabel('Способ расчёта бюджета').selectOption('fixed');
  await first.getByLabel('Фиксированный бюджет').fill('3');
  await first.getByRole('button', { name: 'Сохранить бюджет' }).click();
  await expect(second.locator('.voting-summary')).toContainText('Бюджет: 3');
  await expect(second.locator('.voting-summary')).toContainText('Осталось: 1');
  await expect(late.locator('.voting-summary')).toContainText('Осталось: 2');
  await expect(late.locator('.voting-summary')).toContainText('Исходных карточек: 3');

  await first.getByLabel('Участник').selectOption({ label: 'Второй M5' });
  first.once('dialog', (dialog) => dialog.accept());
  await first.getByRole('button', { name: 'Исключить', exact: true }).click();
  await expect(second.getByRole('heading', { name: 'Доступ к встрече закрыт' })).toBeVisible();
  await expect(firstGroupTopic).toContainText('Всего голосов: 2');
  await expect(firstGroupTopic.getByLabel('Расклад голосов')).toContainText('Второй M5: 2');

  await Promise.all([firstContext, secondContext, lateContext].map(closeContextGracefully));
});
